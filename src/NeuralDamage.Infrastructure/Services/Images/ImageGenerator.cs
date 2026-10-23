using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Feed;

namespace NeuralDamage.Infrastructure.Services.Images;

/// <summary>A picture someone asked for.</summary>
/// <param name="UserId">The person asking; null when a bot is.</param>
/// <param name="BotId">The bot asking; null when a person is.</param>
/// <param name="FaceBotId">The bot to put in the picture with its own face, from its profile picture.</param>
public record ImageAsk(string Prompt, Guid? UserId, Guid? BotId, Guid? FaceBotId = null, int Width = ImageGenerator.PostSize, int Height = ImageGenerator.PostSize);

/// <summary>A picture on its way: its row, and a task that says whether it turned out.</summary>
public record ImageTicket(GeneratedImage Image, Task<bool> Done);

/// <summary>
/// Makes pictures: checks the daily cap, records the picture as pending,
/// queues it for the one GPU (<see cref="ImageQueue"/>) and stores it like an
/// upload when it is ready, or marks it failed. A face asked for without a
/// profile picture to take it from becomes a plain scene.
/// </summary>
public class ImageGenerator(
    IServiceScopeFactory scopes,
    ImageQueue queue,
    IAttachmentStorage storage,
    IOptions<ImageGenOptions> options,
    ILogger<ImageGenerator> logger,
    BotClock? clock = null,
    TimeProvider? time = null)
{
    /// <summary>A post's picture is square.</summary>
    public const int PostSize = 1024;

    private readonly ImageGenOptions _options = options.Value;
    private readonly BotClock _clock = clock ?? BotClock.Default;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsEnabled => _options.Enabled;

    /// <summary>Pictures waiting for the GPU or being drawn.</summary>
    public int Waiting => queue.Waiting;

    public async Task<Result<ImageTicket>> StartAsync(ImageAsk ask, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return Result.Failure<ImageTicket>("Image generation is not set up on this server.");
        var prompt = ask.Prompt.Trim();
        if (prompt.Length == 0)
            return Result.Failure<ImageTicket>("Describe the picture first.");
        if (prompt.Length > GeneratedImage.MaxPromptLength)
            return Result.Failure<ImageTicket>($"A picture's description can be at most {GeneratedImage.MaxPromptLength} characters.");

        GeneratedImage image;
        // The count and the new row go together, or two asks at once could both slip under the cap.
        await _gate.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            var startOfDay = FeedSchedule.StartOfDay(_time.GetUtcNow(), _clock);
            if (await db.GeneratedImages.CountAsync(i => i.CreatedAt >= startOfDay, ct) >= _options.MaxPerDay)
                return Result.Failure<ImageTicket>("That's all the pictures for today. Try again tomorrow.");
            if (ask.UserId is { } userId && await db.GeneratedImages.AnyAsync(i => i.UserId == userId && i.Status == GeneratedImageStatus.Pending, ct))
                return Result.Failure<ImageTicket>("Your last picture is still being drawn. Wait for it first.");

            image = new GeneratedImage
            {
                UserId = ask.UserId,
                BotId = ask.BotId,
                FaceBotId = ask.FaceBotId,
                Prompt = prompt,
                CreatedAt = _time.GetUtcNow().UtcDateTime,
            };
            db.GeneratedImages.Add(image);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _gate.Release();
        }

        // The picture is drawn whatever happens to the request that asked for it.
        var done = RunAsync(image.Id, ask with { Prompt = prompt });
        return Result.Success(new ImageTicket(image, done));
    }

    private async Task<bool> RunAsync(Guid imageId, ImageAsk ask)
    {
        var watch = Stopwatch.StartNew();
        byte[]? png = null;
        string? error = null;
        var faceUsed = false;
        try
        {
            var reference = ask.FaceBotId is { } face && await storage.ReadBotAvatarAsync(face) is { } avatar
                ? ReferenceImage.Prepare(avatar)
                : null;
            faceUsed = reference is not null;
            var request = reference is null
                ? new RenderRequest(ImageWorkflows.Scene, ask.Prompt, ask.Width, ask.Height, Seed())
                : new RenderRequest(ImageWorkflows.Self, ImageWorkflows.SelfPrompt(ask.Prompt), ask.Width, ask.Height, Seed(), reference);
            png = await queue.EnqueueAsync(async token =>
            {
                // A fresh client per picture, so the HTTP handler is rotated as the factory intends.
                using var scope = scopes.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<IImageRenderer>().RenderAsync(request, token);
            });
            if (ImageInspector.Inspect(png) is null)
            {
                png = null;
                error = "The image generator sent back something that is not a picture.";
            }
        }
        catch (TimeoutException ex)
        {
            error = ex.Message;
        }
        catch (ImageGenerationException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Generating picture {ImageId} failed", imageId);
            error = "The image generator failed.";
        }

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            var image = await db.GeneratedImages.FirstOrDefaultAsync(i => i.Id == imageId);
            if (image is null)
                return false; // Discarded meanwhile.

            if (png is not null && ImageInspector.Inspect(png) is { } info)
            {
                await storage.SaveGeneratedImageAsync(imageId, png);
                image.Status = GeneratedImageStatus.Ready;
                image.ContentType = info.ContentType;
                image.SizeBytes = png.Length;
                image.Width = info.Width;
                image.Height = info.Height;
                if (!faceUsed)
                    image.FaceBotId = null;
                logger.LogInformation("Drew picture {ImageId} ({Workflow}) in {Seconds:0.0} s", imageId, faceUsed ? ImageWorkflows.Self : ImageWorkflows.Scene, watch.Elapsed.TotalSeconds);
            }
            else
            {
                image.Status = GeneratedImageStatus.Failed;
                image.Error = error is { Length: > 300 } ? error[..300] : error;
                logger.LogWarning("Picture {ImageId} failed after {Seconds:0.0} s: {Error}", imageId, watch.Elapsed.TotalSeconds, error);
            }
            await db.SaveChangesAsync();
            return image.Status == GeneratedImageStatus.Ready;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Storing picture {ImageId} failed", imageId);
            return false;
        }
    }

    private static long Seed() => Random.Shared.NextInt64(0, 1L << 48);
}
