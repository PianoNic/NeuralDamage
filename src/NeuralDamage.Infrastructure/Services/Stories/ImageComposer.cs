using Microsoft.Extensions.Logging;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Infrastructure.Services.Stories;

/// <summary>
/// A post's picture made from layers, like a story but in a post's format
/// (4:5 or square): rendered by the same renderer, then stored as a ready
/// picture with its spec, for the post to take like a generated one.
/// </summary>
public class ImageComposer(NeuralDamageDbContext db, IAttachmentStorage storage, StoryPublisher publisher, ILogger<ImageComposer> logger)
{
    /// <param name="userId">The person composing it, whose uploads and pictures it may use.</param>
    /// <param name="botId">The bot composing it, whose generated pictures it may use.</param>
    public async Task<Result<GeneratedImage>> ComposeAsync(StorySpec spec, Guid? userId, Guid? botId, CancellationToken ct = default)
    {
        var format = StoryFormats.Of(spec);
        if (!StoryFormats.IsPost(format))
            return Result.Failure<GeneratedImage>("A post's picture is 4:5 (post-4x5) or square (post-1x1).");
        spec = spec with { Format = format };

        var png = await publisher.RenderAsync(spec, userId, ct, botId);
        if (png.IsFailure)
            return Result.Failure<GeneratedImage>(png.Error!);

        spec = await publisher.WithSourcesAsync(spec, ct);
        var (width, height) = StoryFormats.Canvas(format);
        var description = StorySpecRules.Describe(spec);
        var image = new GeneratedImage
        {
            UserId = userId,
            BotId = userId is null ? botId : null,
            Kind = GeneratedImageKind.Composed,
            Prompt = description.Length == 0 ? "A picture" : description.Length > GeneratedImage.MaxPromptLength ? description[..GeneratedImage.MaxPromptLength] : description,
            Spec = StorySpecRules.Serialize(spec),
            SourceUrl = spec.Sources?.FirstOrDefault(),
            Status = GeneratedImageStatus.Ready,
            ContentType = "image/png",
            SizeBytes = png.Value!.Length,
            Width = width,
            Height = height,
        };
        await storage.SaveGeneratedImageAsync(image.Id, png.Value, ct);
        db.GeneratedImages.Add(image);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Composed picture {ImageId} ({Format}, {Layers} layer(s)) for {Who}", image.Id, format, spec.Layers.Count, userId is null ? "a bot" : "a person");
        return Result.Success(image);
    }
}
