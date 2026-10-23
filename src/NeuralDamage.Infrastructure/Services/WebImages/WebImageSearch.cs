using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NeuralDamage.Infrastructure.Services.WebImages;

/// <summary>The image web search, bound from the <c>ImageSearch</c> section.</summary>
public class ImageSearchOptions
{
    public const string SectionName = "ImageSearch";

    /// <summary>The SearXNG instance, e.g. <c>http://searxng:8080</c>. Unset turns the feature off.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>How long one search may take; engines that are slower are left out by SearXNG itself.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    public Uri? BaseUri => Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri) ? uri : null;

    public bool Enabled => BaseUri is not null;
}

/// <summary>One picture a search found.</summary>
/// <param name="ImageUrl">The picture itself.</param>
/// <param name="ThumbnailUrl">A small version for a results grid, when the engine has one.</param>
/// <param name="SourceUrl">The page it is on, to credit.</param>
public record WebImageResult(string Title, Uri ImageUrl, Uri? ThumbnailUrl, Uri SourceUrl, int? Width, int? Height, string? Engine);

public interface IWebImageSearch
{
    bool IsEnabled { get; }

    /// <summary>Pictures for <paramref name="query"/>, best first as the engines rank them; none when the search fails.</summary>
    Task<IReadOnlyList<WebImageResult>> SearchAsync(string query, CancellationToken ct = default);
}

/// <summary>
/// Searches pictures with a self-hosted SearXNG (<c>/search?format=json&amp;categories=images</c>).
/// Engines that fail or answer with a CAPTCHA are SearXNG's problem: it
/// lists them as unresponsive and returns what the others found.
/// </summary>
public partial class SearxngImageSearch(HttpClient http, IOptions<ImageSearchOptions> options, ILogger<SearxngImageSearch> logger) : IWebImageSearch
{
    public const int MaxResults = 30;
    public const int MaxQueryLength = 120;
    private const int MaxJsonBytes = 2 * 1024 * 1024;

    private readonly ImageSearchOptions _options = options.Value;

    public bool IsEnabled => _options.Enabled;

    public async Task<IReadOnlyList<WebImageResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (!IsEnabled || query.Length == 0)
            return [];
        if (query.Length > MaxQueryLength)
            query = query[..MaxQueryLength];

        var url = new Uri(_options.BaseUri!, $"search?q={Uri.EscapeDataString(query)}&categories=images&format=json&safesearch=1&language=all");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.Timeout);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Image search answered {Status}", (int)response.StatusCode);
                return [];
            }
            if (response.Content.Headers.ContentLength > MaxJsonBytes)
                return [];
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            using var json = JsonDocument.Parse(body);
            var (results, unresponsive) = Parse(json.RootElement);
            logger.LogInformation("Image search found {Count} picture(s){Unresponsive}", results.Count,
                unresponsive.Count == 0 ? "" : $"; engines that failed: {string.Join(", ", unresponsive)}");
            return results;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Image search took longer than {Seconds} s", _options.Timeout.TotalSeconds);
            return [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            logger.LogWarning("Image search failed: {Reason}", ex.Message);
            return [];
        }
    }

    /// <summary>The pictures in a SearXNG JSON answer, in its order, and the engines it says failed.</summary>
    public static (List<WebImageResult> Results, List<string> Unresponsive) Parse(JsonElement root)
    {
        var results = new List<WebImageResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (results.Count >= MaxResults)
                    break;
                if (Web(Text(item, "img_src")) is not { } image || !seen.Add(image.AbsoluteUri))
                    continue;
                var page = Web(Text(item, "url")) ?? image;
                var (width, height) = Resolution(Text(item, "resolution"));
                if (width is null && Number(item, "img_width") is { } w && Number(item, "img_height") is { } h)
                    (width, height) = (w, h);
                var title = (Text(item, "title") ?? "").Trim();
                results.Add(new WebImageResult(title.Length > 200 ? title[..200] : title, image, Web(Text(item, "thumbnail_src")), page, width, height, Text(item, "engine")));
            }
        }

        var unresponsive = new List<string>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("unresponsive_engines", out var failed) && failed.ValueKind == JsonValueKind.Array)
            foreach (var engine in failed.EnumerateArray())
                if (engine.ValueKind == JsonValueKind.Array && engine.GetArrayLength() > 0 && engine[0].GetString() is { } name)
                    unresponsive.Add(engine.GetArrayLength() > 1 && engine[1].GetString() is { } why ? $"{name} ({why})" : name);
        return (results, unresponsive);
    }

    /// <summary>An absolute http(s) URL; protocol-relative ones ("//host/x.jpg") are taken as https.</summary>
    private static Uri? Web(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.StartsWith("//", StringComparison.Ordinal))
            value = "https:" + value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;
    }

    /// <summary>"1920 x 1080", "1920x1080" or "1920 × 1080".</summary>
    private static (int?, int?) Resolution(string? value)
    {
        var match = value is null ? null : ResolutionPattern().Match(value);
        return match is { Success: true }
            ? (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : (null, null);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : null;

    [GeneratedRegex(@"^\s*(\d{1,5})\s*[x×]\s*(\d{1,5})\s*$")]
    private static partial Regex ResolutionPattern();
}

/// <summary>
/// Picks the picture to use from a search: large ones whose shape matches what
/// it is for, tiny ones never; then fetches them in that order until one works.
/// </summary>
public static class WebImagePicker
{
    /// <summary>Pictures smaller than this on their short side are too small to use.</summary>
    public const int MinSide = 300;

    /// <summary>How many results are tried before giving up.</summary>
    public const int MaxTries = 5;

    /// <summary>The results worth trying, best first, for a picture of <paramref name="aspect"/> (width / height).</summary>
    public static List<WebImageResult> Rank(IEnumerable<WebImageResult> results, double aspect) =>
        results
            .Select((r, i) => (Result: r, Index: i))
            .Where(x => x.Result.Width is not { } w || x.Result.Height is not { } h || Math.Min(w, h) >= MinSide)
            .OrderByDescending(x => Score(x.Result, aspect))
            .ThenBy(x => x.Index)
            .Select(x => x.Result)
            .ToList();

    /// <summary>Bigger is better up to about 1500 px, a matching shape is better, an unknown size is middling.</summary>
    public static double Score(WebImageResult result, double aspect)
    {
        if (result.Width is not { } w || result.Height is not { } h || w <= 0 || h <= 0)
            return 1.0;
        var size = Math.Min(Math.Min(w, h), 1500) / 1500.0;
        var shape = Math.Abs(Math.Log((double)w / h / aspect));
        return 0.5 + size - shape;
    }

    /// <summary>The first of the ranked results that fetches as a picture big enough; null when none does.</summary>
    public static async Task<(WebImageResult Result, FetchedImage Image)?> FetchBestAsync(
        IReadOnlyList<WebImageResult> results, double aspect, SafeImageFetcher fetcher, CancellationToken ct = default)
    {
        foreach (var result in Rank(results, aspect).Take(MaxTries))
        {
            var image = await fetcher.FetchAsync(result.ImageUrl, ct);
            if (image is not null && Math.Min(image.Width, image.Height) >= MinSide)
                return (result, image);
        }
        return null;
    }
}
