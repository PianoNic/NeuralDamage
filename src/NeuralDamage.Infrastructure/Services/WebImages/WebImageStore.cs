using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Infrastructure.Services.WebImages;

/// <summary>
/// Pictures from the web for posts and stories: searched, fetched through the
/// <see cref="SafeImageFetcher"/> and stored like a generated picture
/// (<see cref="GeneratedImageKind.Web"/>) with the page they came from, so a
/// post or a spec can use them as <c>generated:&lt;id&gt;</c>. People pick one
/// from the results by its place in the list; nothing they send is fetched.
/// </summary>
public class WebImageStore(
    NeuralDamageDbContext db,
    IAttachmentStorage storage,
    IWebImageSearch search,
    SafeImageFetcher fetcher,
    IMemoryCache cache,
    ILogger<WebImageStore> logger)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    public bool IsEnabled => search.IsEnabled;

    /// <summary>The results for <paramref name="query"/>, kept for a while so a pick refers to what the person saw.</summary>
    public async Task<IReadOnlyList<WebImageResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0 || !IsEnabled)
            return [];
        var key = Key(query);
        if (cache.TryGetValue(key, out IReadOnlyList<WebImageResult>? cached) && cached is not null)
            return cached;
        var results = await search.SearchAsync(query, ct);
        if (results.Count > 0)
            cache.Set(key, results, CacheFor);
        return results;
    }

    /// <summary>A small picture of result <paramref name="index"/> for the results grid; null when it cannot be fetched.</summary>
    public async Task<FetchedImage?> ThumbnailAsync(string query, int index, CancellationToken ct = default)
    {
        var results = await SearchAsync(query, ct);
        if (index < 0 || index >= results.Count)
            return null;
        var key = $"{Key(query)}#thumb{index}";
        if (cache.TryGetValue(key, out FetchedImage? cached) && cached is not null)
            return cached;
        var result = results[index];
        var image = (result.ThumbnailUrl is { } thumb ? await fetcher.FetchAsync(thumb, ct) : null)
                    ?? await fetcher.FetchAsync(result.ImageUrl, ct);
        if (image is not null)
            cache.Set(key, image, CacheFor);
        return image;
    }

    /// <summary>Stores result <paramref name="index"/> of a search the person made, for their post or story.</summary>
    public async Task<Result<GeneratedImage>> PickAsync(Guid userId, string query, int index, CancellationToken ct = default)
    {
        var results = await SearchAsync(query, ct);
        if (index < 0 || index >= results.Count)
            return Result.Failure<GeneratedImage>("That search result is gone. Search again.");
        var result = results[index];
        var image = await fetcher.FetchAsync(result.ImageUrl, ct);
        if (image is null && result.ThumbnailUrl is { } thumb)
            image = await fetcher.FetchAsync(thumb, ct);
        if (image is null)
            return Result.Failure<GeneratedImage>("That picture cannot be used. Pick another one.");
        return Result.Success(await SaveAsync(result, image, query, userId, null, ct));
    }

    /// <summary>
    /// Searches for a bot and stores the best picture for a canvas of
    /// <paramref name="aspect"/> (width / height); null when nothing usable came back.
    /// </summary>
    public async Task<GeneratedImage?> FindForBotAsync(Guid botId, string query, double aspect, CancellationToken ct = default)
    {
        var results = await SearchAsync(query, ct);
        var best = await WebImagePicker.FetchBestAsync(results, aspect, fetcher, ct);
        if (best is null)
        {
            logger.LogInformation("Web search for a bot's picture found nothing usable ({Count} result(s))", results.Count);
            return null;
        }
        return await SaveAsync(best.Value.Result, best.Value.Image, query, null, botId, ct);
    }

    private async Task<GeneratedImage> SaveAsync(WebImageResult result, FetchedImage fetched, string query, Guid? userId, Guid? botId, CancellationToken ct)
    {
        var what = result.Title.Length > 0 ? result.Title : query;
        var source = result.SourceUrl.AbsoluteUri;
        var image = new GeneratedImage
        {
            UserId = userId,
            BotId = botId,
            Kind = GeneratedImageKind.Web,
            Prompt = what.Length > GeneratedImage.MaxPromptLength ? what[..GeneratedImage.MaxPromptLength] : what,
            SourceUrl = source.Length > GeneratedImage.MaxSourceUrlLength ? fetched.Url.GetLeftPart(UriPartial.Authority) : source,
            Status = GeneratedImageStatus.Ready,
            ContentType = fetched.ContentType,
            SizeBytes = fetched.Content.Length,
            Width = fetched.Width,
            Height = fetched.Height,
        };
        await storage.SaveGeneratedImageAsync(image.Id, fetched.Content, ct);
        db.GeneratedImages.Add(image);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Stored web picture {ImageId} from {Host} ({Width}x{Height}) for {Who}", image.Id, fetched.Url.Host, fetched.Width, fetched.Height, userId is null ? "a bot" : "a person");
        return image;
    }

    private static string Key(string query) => "web-images:" + query.Trim().ToLowerInvariant();
}
