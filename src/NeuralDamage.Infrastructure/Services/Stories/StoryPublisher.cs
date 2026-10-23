using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.BotPictures;
using NeuralDamage.Infrastructure.Services.Feed;

namespace NeuralDamage.Infrastructure.Services.Stories;

/// <summary>
/// Turns a spec into a story, the same way for a person and for a bot: checks
/// it, fetches its pictures (the author's own uploads, stickers from the
/// library, pictures from the Wikimedia lookup), renders the PNG, stores it
/// and tells everyone.
/// </summary>
public class StoryPublisher(
    NeuralDamageDbContext db,
    IAttachmentStorage storage,
    StoryRenderer renderer,
    IBotPictureFinder pictures,
    IFeedNotifier notifier,
    ILogger<StoryPublisher> logger)
{
    /// <summary>The PNG of <paramref name="spec"/>, or why it cannot be one. Nothing is stored.</summary>
    /// <param name="uploaderUserId">Whose uploads the spec may use; a bot has none.</param>
    /// <param name="botId">The bot making it, whose generated pictures the spec may use.</param>
    public async Task<Result<byte[]>> RenderAsync(StorySpec spec, Guid? uploaderUserId, CancellationToken ct = default, Guid? botId = null)
    {
        var stickerNames = await db.Stickers.Where(s => s.DeletedAt == null).Select(s => s.Name).ToListAsync(ct);
        var known = stickerNames.ToHashSet(StringComparer.Ordinal);
        var errors = StorySpecRules.Validate(spec, known.Contains);
        if (errors.Count > 0)
            return Result<byte[]>.Failure(string.Join(" ", errors.Take(3)));

        var (assets, problem) = await AssetsAsync(spec, uploaderUserId, botId, ct);
        if (problem is not null)
            return Result<byte[]>.Failure(problem);
        return Result<byte[]>.Success(await Task.Run(() => renderer.Render(spec, assets!), ct));
    }

    /// <summary>Renders, stores and announces a story by a person or a bot.</summary>
    public async Task<Result<StoryDto>> PublishAsync(StorySpec spec, Guid? userId, Guid? botId, CancellationToken ct = default)
    {
        if (StoryFormats.Of(spec) != StoryFormats.Story)
            return Result<StoryDto>.Failure("A story is 9:16; that picture is made for a post.");
        var png = await RenderAsync(spec, userId, ct, botId);
        if (png.IsFailure)
            return Result<StoryDto>.Failure(png.Error!);
        spec = await WithSourcesAsync(spec, ct);

        var story = new Story { AuthorUserId = userId, AuthorBotId = botId, Spec = StorySpecRules.Serialize(spec), SizeBytes = png.Value!.Length };
        await storage.SaveStoryFileAsync(story.Id, png.Value, ct);
        db.Stories.Add(story);
        await db.SaveChangesAsync(ct);

        var dto = (await StoryReads.StoriesAsync(db, [story.Id], userId, ct)).Single();
        await notifier.StoryNew(dto with { Seen = false });
        logger.LogInformation("Story {StoryId} posted by {Author}", story.Id, dto.Author.Name);
        return Result<StoryDto>.Success(dto);
    }

    /// <summary>The spec with <see cref="StorySpec.Sources"/> set to the pages its pictures from the web came from, in order.</summary>
    public async Task<StorySpec> WithSourcesAsync(StorySpec spec, CancellationToken ct = default)
    {
        var ids = StorySpecRules.Images(spec).OfType<StoryImageRef.Generated>().Select(g => g.Id).Distinct().ToList();
        var sources = ids.Count == 0
            ? []
            : await db.GeneratedImages.Where(i => ids.Contains(i.Id) && i.Kind == GeneratedImageKind.Web && i.SourceUrl != null)
                .Select(i => new { i.Id, i.SourceUrl }).ToListAsync(ct);
        var ordered = ids.Select(id => sources.FirstOrDefault(s => s.Id == id)?.SourceUrl).OfType<string>().Distinct().ToList();
        return spec with { Sources = ordered.Count == 0 ? null : ordered };
    }

    private async Task<(StoryAssets? Assets, string? Problem)> AssetsAsync(StorySpec spec, Guid? uploaderUserId, Guid? botId, CancellationToken ct)
    {
        byte[]? background = null;
        if (spec.Background.Image is { } bg)
        {
            var (data, problem) = await ImageAsync(bg, uploaderUserId, botId, ct);
            if (problem is not null)
                return (null, problem);
            background = data;
        }

        var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var reference in spec.Layers.Where(l => l.Type == "image").Select(l => l.Image!.Trim()).Distinct())
        {
            var (data, problem) = await ImageAsync(reference, uploaderUserId, botId, ct);
            if (problem is not null)
                return (null, problem);
            images[reference] = data!;
        }

        var names = spec.Layers.Where(l => l.Type == "sticker").Select(l => l.Name!.Trim()).Distinct().ToList();
        var stickerIds = await db.Stickers.Where(s => s.DeletedAt == null && names.Contains(s.Name)).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var stickers = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var sticker in stickerIds)
            if (await storage.ReadStickerAsync(sticker.Id, ct) is { } data)
                stickers[sticker.Name] = data;

        return (new StoryAssets(background, stickers, images), null);
    }

    private async Task<(byte[]? Data, string? Problem)> ImageAsync(string reference, Guid? uploaderUserId, Guid? botId, CancellationToken ct)
    {
        switch (StoryImageRef.Parse(reference))
        {
            case StoryImageRef.Upload upload:
                var mine = uploaderUserId is { } owner && await db.StoryImages.AnyAsync(i => i.Id == upload.Id && i.UploaderUserId == owner, ct);
                var bytes = mine ? await storage.ReadStoryFileAsync(upload.Id, ct) : null;
                return bytes is null ? (null, "A picture in the story is gone. Add it again.") : (bytes, null);

            case StoryImageRef.Generated generated:
                var own = await db.GeneratedImages.AnyAsync(i => i.Id == generated.Id && i.Status == GeneratedImageStatus.Ready
                    && ((uploaderUserId != null && i.UserId == uploaderUserId) || (botId != null && i.BotId == botId)), ct);
                var drawn = own ? await storage.ReadGeneratedImageAsync(generated.Id, ct) : null;
                return drawn is null ? (null, "A generated picture in the story is gone. Generate it again.") : (drawn, null);

            case StoryImageRef.Wiki wiki:
                try
                {
                    var found = await pictures.FindAsync(wiki.Name, ct);
                    if (found.Count > wiki.Index && await pictures.DownloadAsync(found[wiki.Index], ct) is { } picture)
                        return (picture.Content, null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Picture lookup for a story failed");
                }
                return (null, $"No picture of \"{wiki.Name}\" was found.");

            default:
                return (null, "A picture in the story is not one this app knows.");
        }
    }
}

/// <summary>Stories as the API hands them out.</summary>
public static class StoryReads
{
    /// <summary>Live stories of people and of public bots; like the feed, never a private or deleted bot's.</summary>
    public static IQueryable<Story> Live(this IQueryable<Story> stories, DateTime now) =>
        stories.Where(s => s.ExpiresAt > now && (s.AuthorUserId != null || (s.AuthorBot != null && s.AuthorBot.IsPublic && s.AuthorBot.IsActive)));

    public static string ImageUrl(Guid storyId) => $"/api/stories/{storyId}/image";

    public static async Task<List<StoryDto>> StoriesAsync(NeuralDamageDbContext db, IReadOnlyCollection<Guid> ids, Guid? viewerId, CancellationToken ct = default)
    {
        var stories = await db.Stories.Where(s => ids.Contains(s.Id))
            .Include(s => s.AuthorUser)
            .Include(s => s.AuthorBot)
            .AsNoTracking()
            .ToListAsync(ct);
        var seen = viewerId is { } viewer
            ? (await db.StoryViews.Where(v => v.UserId == viewer && ids.Contains(v.StoryId)).Select(v => v.StoryId).ToListAsync(ct)).ToHashSet()
            : [];
        return stories
            .OrderBy(s => s.CreatedAt)
            .Select(s => new StoryDto(
                s.Id,
                FeedReads.Author(s.AuthorUser, s.AuthorBot),
                ImageUrl(s.Id),
                s.CreatedAt,
                s.ExpiresAt,
                seen.Contains(s.Id) || (s.AuthorUserId != null && s.AuthorUserId == viewerId),
                StorySpecRules.Parse(s.Spec).Spec is { } spec ? StorySpecRules.Describe(spec) : ""))
            .ToList();
    }
}
