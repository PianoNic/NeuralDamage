using NeuralDamage.Domain;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NeuralDamage.Infrastructure.Services.Attachments;

/// <summary>
/// Writes a description of each uploaded image, once, so bots on text-only
/// models can follow a conversation about it and Jev can rank on it.
/// </summary>
public interface IImageDescriber
{
    /// <summary>Starts describing the image in the background, unless it is described or under way.</summary>
    void Enqueue(Guid attachmentId);

    /// <summary>
    /// Waits for the images' descriptions, starting any that are missing, for
    /// at most <paramref name="timeout"/>. Never throws for a failed description.
    /// </summary>
    Task WaitAsync(IReadOnlyCollection<Guid> attachmentIds, TimeSpan timeout, CancellationToken ct = default);
}

public class ImageDescriber(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ImageDescriber> logger) : IImageDescriber
{
    /// <summary>Cheap, sees images, and has a zero-data-retention endpoint within the default price caps.</summary>
    public const string DefaultModel = "google/gemini-2.5-flash-lite";

    private const int MaxDescriptionChars = 2000;

    public const string Prompt = """
        You describe pictures for people in a group chat who cannot see them.
        Describe the image in detail: what it shows, the setting, people (appearance, expression, what they are doing; never guess who they are), animals, notable objects, colours and mood.
        Copy any visible text word for word. For a meme, screenshot or chart, say what it says and what the point or joke is.
        Plain prose, no markdown, no preamble, at most 150 words.
        """;

    public const string StickerPrompt = """
        You label stickers for a group chat, so that chat partners who cannot see them know when to send which.
        Write one short line: what the sticker shows, then the mood and the moment you'd send it, e.g. "embarrassed monkey covering its eyes, for cringe or oops moments" or "cat crying with laughter, for when something is hilarious".
        Be specific and a little funny. Copy any visible text word for word.
        Name the person, character or meme when you recognise them: famous people, cartoon characters, well-known memes like "distracted boyfriend". Otherwise describe what you see and never make up a name.
        Lowercase, no quotes, no full stop at the end, at most 20 words.
        """;

    // One run per image for the life of the process: whoever asks first starts
    // it, everyone after waits on the same task. A failed run is not retried
    // round after round; a restart gives it another go.
    private readonly ConcurrentDictionary<Guid, Lazy<Task>> _runs = new();

    public string Model { get; } = string.IsNullOrWhiteSpace(configuration["OpenRouter:VisionModel"])
        ? DefaultModel
        : configuration["OpenRouter:VisionModel"]!.Trim();

    public void Enqueue(Guid attachmentId) => _ = Run(attachmentId);

    public async Task WaitAsync(IReadOnlyCollection<Guid> attachmentIds, TimeSpan timeout, CancellationToken ct = default)
    {
        if (attachmentIds.Count == 0)
            return;
        var all = Task.WhenAll(attachmentIds.Select(Run));
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await Task.WhenAny(all, Task.Delay(timeout, delayCts.Token));
        await delayCts.CancelAsync();
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Writes one sticker's one-line description now; the sticker describe queue
    /// runs this a few at a time. A failure leaves it without one: people can
    /// still send it, bots cannot until its uploader writes one.
    /// </summary>
    public async Task DescribeStickerAsync(Guid stickerId, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            string? text = null;
            try
            {
                var sticker = await db.Stickers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == stickerId, ct);
                if (sticker is null || sticker.Description is not null)
                    return;

                var openRouter = scope.ServiceProvider.GetRequiredService<IOpenRouterService>();
                if (await RefusalAsync(openRouter, scope.ServiceProvider.GetRequiredService<ModelPolicy>()) is { } refusal)
                {
                    logger.LogWarning("Sticker {StickerId} left undescribed: {Reason}", stickerId, refusal);
                    return;
                }

                var data = await scope.ServiceProvider.GetRequiredService<IAttachmentStorage>().ReadStickerAsync(stickerId, ct);
                if (data is null)
                    return;

                var request = new ChatMessage("user", "Label this sticker.") { Images = [new ImagePart(sticker.ContentType, data)] };
                text = CleanStickerLine(await openRouter.GenerateResponseAsync(Model, 0.4, StickerPrompt, [request], ct));
                logger.LogInformation("Described sticker {StickerId} with {Model}", stickerId, Model);
            }
            finally
            {
                // Read fresh: the uploader may have written one by hand meanwhile, which wins.
                // Cut short by shutdown, it stays marked, so the next start queues it again.
                var stored = ct.IsCancellationRequested ? null : await db.Stickers.FirstOrDefaultAsync(s => s.Id == stickerId);
                if (stored is not null)
                {
                    if (stored.Description is null && !string.IsNullOrEmpty(text))
                        stored.Description = text;
                    stored.Describing = false;
                    await db.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Describing sticker {StickerId} with {Model} failed", stickerId, Model);
        }
    }

    /// <summary>One line, without the quotes and full stop models like to add, cut to the column's length.</summary>
    public static string? CleanStickerLine(string? text)
    {
        var line = (text ?? "").Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        line = line.Trim('"', '\'', '“', '”', ' ').TrimEnd('.');
        if (line.Length > Stickers.StickerRules.MaxDescriptionLength)
            line = line[..Stickers.StickerRules.MaxDescriptionLength].TrimEnd();
        return line.Length == 0 ? null : line;
    }

    private Task Run(Guid attachmentId) =>
        _runs.GetOrAdd(attachmentId, id => new Lazy<Task>(() => Task.Run(() => DescribeAsync(id)))).Value;

    private async Task DescribeAsync(Guid attachmentId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            var attachment = await db.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId);
            if (attachment is null || attachment.Description is not null)
                return;

            var openRouter = scope.ServiceProvider.GetRequiredService<IOpenRouterService>();
            if (await RefusalAsync(openRouter, scope.ServiceProvider.GetRequiredService<ModelPolicy>()) is { } refusal)
            {
                logger.LogWarning("Image {AttachmentId} left undescribed: {Reason}", attachmentId, refusal);
                return;
            }

            var data = await scope.ServiceProvider.GetRequiredService<IAttachmentStorage>().ReadAllAsync(attachment.ChatId, attachment.Id);
            if (data is null)
                return;

            var request = new ChatMessage("user", "Describe this image.") { Images = [new ImagePart(attachment.ContentType, data)] };
            var text = (await openRouter.GenerateResponseAsync(Model, 0.2, Prompt, [request])).Trim();
            if (text.Length == 0)
            {
                logger.LogWarning("Describer {Model} returned nothing for image {AttachmentId}", Model, attachmentId);
                return;
            }

            attachment.Description = text.Length > MaxDescriptionChars ? text[..MaxDescriptionChars] : text;
            await db.SaveChangesAsync();
            logger.LogInformation("Described image {AttachmentId} with {Model} ({Chars} chars)", attachmentId, Model, attachment.Description.Length);

            await AnnounceAsync(scope.ServiceProvider, db, attachment);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Describing image {AttachmentId} with {Model} failed", attachmentId, Model);
        }
    }

    /// <summary>
    /// Tells the chat, so the image gets its alt text and caption without a
    /// reload. Only once it is on a sent message: an upload still sitting in
    /// someone's composer is nobody else's business. One described before it
    /// was sent goes out with the message itself.
    /// </summary>
    private async Task AnnounceAsync(IServiceProvider services, NeuralDamageDbContext db, Attachment attachment)
    {
        // Read fresh: the message may have been sent while the describer ran.
        var messageId = await db.Attachments
            .Where(a => a.Id == attachment.Id)
            .Select(a => a.MessageId)
            .FirstOrDefaultAsync();
        if (messageId is not { } sentOn)
            return;

        await services.GetRequiredService<IChatNotificationService>()
            .NotifyAttachmentDescribed(attachment.ChatId, sentOn, attachment.Id, attachment.Description!);
    }

    /// <summary>
    /// The describer is held to the same model policy as the bots, and never
    /// uses a :free variant: those are rate-limited and may log prompts.
    /// </summary>
    private async Task<string?> RefusalAsync(IOpenRouterService openRouter, ModelPolicy policy)
    {
        if (Model.EndsWith(":free", StringComparison.Ordinal))
            return $"OpenRouter:VisionModel '{Model}' is a :free variant, which is not used for describing images.";
        if (await policy.CheckModelAsync(openRouter, Model) is { } refusal)
            return refusal;

        var models = await openRouter.ListModelsAsync();
        var model = models?.FirstOrDefault(m => m.Id == Model);
        if (model is not null && !model.Capabilities.Contains(ModelMetadata.Vision))
            return $"OpenRouter:VisionModel '{Model}' cannot see images.";
        return null;
    }
}
