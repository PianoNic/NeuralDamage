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
    public const string DefaultModel = "google/gemma-3-12b-it";

    private const int MaxDescriptionChars = 2000;

    public const string Prompt = """
        You describe pictures for people in a group chat who cannot see them.
        Describe the image in detail: what it shows, the setting, people (appearance, expression, what they are doing; never guess who they are), animals, notable objects, colours and mood.
        Copy any visible text word for word. For a meme, screenshot or chart, say what it says and what the point or joke is.
        Plain prose, no markdown, no preamble, at most 150 words.
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
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Describing image {AttachmentId} with {Model} failed", attachmentId, Model);
        }
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
