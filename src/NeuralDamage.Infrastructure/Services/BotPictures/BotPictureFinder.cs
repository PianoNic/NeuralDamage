using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Infrastructure.Services.BotPictures;

/// <param name="Title">The article or file the picture comes from.</param>
/// <param name="SourceUrl">The page to credit, on Wikipedia or Commons.</param>
/// <param name="ImageUrl">The picture itself, on a Wikimedia media host.</param>
public record BotPictureCandidate(string Title, string SourceUrl, Uri ImageUrl);

public record BotPictureImage(byte[] Content, string ContentType);

public interface IBotPictureFinder
{
    /// <summary>Up to three pictures for a bot called <paramref name="name"/>; none when nothing notable has that name.</summary>
    Task<IReadOnlyList<BotPictureCandidate>> FindAsync(string name, CancellationToken ct);

    /// <summary>The bytes of one of <see cref="FindAsync"/>'s candidates, or null when it cannot be fetched or is not an image.</summary>
    Task<BotPictureImage?> DownloadAsync(BotPictureCandidate candidate, CancellationToken ct);
}

/// <summary>
/// Looks up a picture for a bot's name on Wikipedia (the article's lead image, English first, then
/// German), topped up from a Wikimedia Commons file search when the name is an article. Every request,
/// redirects included, goes to a fixed list of Wikimedia hosts: the client never supplies a URL.
/// </summary>
public partial class BotPictureFinder(HttpClient http, IMemoryCache cache, AttachmentOptions attachments, ILogger<BotPictureFinder> logger)
    : IBotPictureFinder
{
    public const int MaxCandidates = 3;

    /// <summary>A Wikimedia standard thumbnail step: plenty for a 256px avatar, far smaller than most originals.</summary>
    public const int ThumbnailWidth = 960;

    /// <summary>Generic, with nothing about the user or the server it runs on.</summary>
    public const string UserAgent = "NeuralDamage/1.0";

    private const int MaxRedirects = 3;
    private const int MaxJsonBytes = 512 * 1024;
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private static readonly HashSet<string> ApiHosts = ["en.wikipedia.org", "de.wikipedia.org", "commons.wikimedia.org"];
    private static readonly HashSet<string> MediaHosts = ["upload.wikimedia.org", "thumb.wikimedia.org"];
    private static readonly string[] Languages = ["en", "de"];
    private static readonly HashSet<string> CommonsTypes = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>Whether the server may fetch <paramref name="uri"/>: HTTPS on a Wikimedia API or media host, default port.</summary>
    public static bool IsAllowed(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && (ApiHosts.Contains(uri.Host) || MediaHosts.Contains(uri.Host));

    public static bool IsMediaHost(Uri uri) => IsAllowed(uri) && MediaHosts.Contains(uri.Host);

    public async Task<IReadOnlyList<BotPictureCandidate>> FindAsync(string name, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length == 0)
            return [];

        var key = "bot-picture:" + name.ToLowerInvariant();
        if (cache.TryGetValue(key, out IReadOnlyList<BotPictureCandidate>? cached) && cached is not null)
            return cached;

        var found = await LookUpAsync(name, ct);
        cache.Set(key, found, CacheFor);
        return found;
    }

    private async Task<IReadOnlyList<BotPictureCandidate>> LookUpAsync(string name, CancellationToken ct)
    {
        var candidates = new List<BotPictureCandidate>();
        string? article = null;

        foreach (var language in Languages)
        {
            var summary = await SummaryAsync(language, name, ct);
            if (summary is null)
                continue;
            article = summary.Value.Title;
            if (summary.Value.Picture is { } picture)
                candidates.Add(picture);
            break;
        }

        // A name with no article is a persona, not someone with a picture: Commons would only match its words.
        if (article is not null)
        {
            foreach (var file in await CommonsAsync(article, ct))
            {
                if (candidates.Count >= MaxCandidates)
                    break;
                if (!candidates.Any(c => SameImage(c.ImageUrl, file.ImageUrl)))
                    candidates.Add(file);
            }
        }

        return candidates;
    }

    /// <summary>The article's title and lead image, or null when there is no such article (or only a disambiguation page).</summary>
    private async Task<(string Title, BotPictureCandidate? Picture)?> SummaryAsync(string language, string name, CancellationToken ct)
    {
        var title = Uri.EscapeDataString(name.Replace(' ', '_'));
        using var json = await GetJsonAsync(new Uri($"https://{language}.wikipedia.org/api/rest_v1/page/summary/{title}"), ct);
        if (json is null)
            return null;

        var root = json.RootElement;
        if (Text(root, "type") != "standard" || Text(root, "title") is not { } articleTitle)
            return null;

        var page = root.TryGetProperty("content_urls", out var urls) && urls.TryGetProperty("desktop", out var desktop)
            ? Text(desktop, "page")
            : null;
        page ??= $"https://{language}.wikipedia.org/wiki/{title}";

        var image = LeadImage(root);
        return (articleTitle, image is null ? null : new BotPictureCandidate(articleTitle, page, image));
    }

    /// <summary>
    /// The summary's lead image at a sensible size: the original when it is small, otherwise its
    /// thumbnail asked for at <see cref="ThumbnailWidth"/>. SVGs are left out: on an article they are logos.
    /// </summary>
    public static Uri? LeadImage(JsonElement summary)
    {
        if (!summary.TryGetProperty("originalimage", out var original) || Text(original, "source") is not { } originalSource)
            return null;
        if (!Uri.TryCreate(originalSource, UriKind.Absolute, out var originalUri) || originalUri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return null;

        var width = original.TryGetProperty("width", out var w) && w.TryGetInt32(out var n) ? n : int.MaxValue;
        Uri? chosen = originalUri;
        if (width > ThumbnailWidth
            && summary.TryGetProperty("thumbnail", out var thumbnail)
            && Text(thumbnail, "source") is { } thumbSource
            && ThumbWidth().IsMatch(thumbSource))
        {
            Uri.TryCreate(ThumbWidth().Replace(thumbSource, $"/{ThumbnailWidth}px-", 1), UriKind.Absolute, out chosen);
        }

        return chosen is not null && IsMediaHost(chosen) ? chosen : null;
    }

    private async Task<IReadOnlyList<BotPictureCandidate>> CommonsAsync(string article, CancellationToken ct)
    {
        var search = Uri.EscapeDataString($"\"{article}\"");
        var url = new Uri("https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search"
                          + $"&gsrsearch={search}&gsrnamespace=6&gsrlimit=8&prop=imageinfo&iiprop=url%7Cmime&iiurlwidth={ThumbnailWidth}");
        using var json = await GetJsonAsync(url, ct);
        return json is null ? [] : ParseCommons(json.RootElement);
    }

    /// <summary>The photos in a Commons search answer, in search order; drawings, PDFs and SVGs are left out.</summary>
    public static IReadOnlyList<BotPictureCandidate> ParseCommons(JsonElement root)
    {
        if (!root.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Object)
            return [];

        var found = new List<(int Index, BotPictureCandidate Candidate)>();
        foreach (var page in pages.EnumerateObject().Select(p => p.Value))
        {
            if (!page.TryGetProperty("imageinfo", out var infos) || infos.ValueKind != JsonValueKind.Array || infos.GetArrayLength() == 0)
                continue;
            var info = infos[0];
            if (Text(info, "mime") is not { } mime || !CommonsTypes.Contains(mime))
                continue;
            if ((Text(info, "thumburl") ?? Text(info, "url")) is not { } source
                || !Uri.TryCreate(source, UriKind.Absolute, out var image) || !IsMediaHost(image))
                continue;

            var title = Text(page, "title") ?? "";
            if (title.StartsWith("File:", StringComparison.Ordinal))
                title = title[5..];
            title = Path.GetFileNameWithoutExtension(title);
            var sourceUrl = Text(info, "descriptionurl") ?? "https://commons.wikimedia.org/";
            var index = page.TryGetProperty("index", out var i) && i.TryGetInt32(out var n) ? n : int.MaxValue;
            found.Add((index, new BotPictureCandidate(title, sourceUrl, image)));
        }

        return [.. found.OrderBy(f => f.Index).Select(f => f.Candidate)];
    }

    public async Task<BotPictureImage?> DownloadAsync(BotPictureCandidate candidate, CancellationToken ct)
    {
        if (!IsMediaHost(candidate.ImageUrl))
            return null;

        try
        {
            using var response = await SendAsync(candidate.ImageUrl, ct);
            if (response is null || !response.IsSuccessStatusCode || !IsMediaHost(response.RequestMessage!.RequestUri!))
                return null;
            var data = await ReadCappedAsync(response, attachments.MaxBytes, ct);
            return data is not null && ImageInspector.Inspect(data) is { } image ? new BotPictureImage(data, image.ContentType) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogInformation("Could not download the suggested picture {Url}: {Reason}", candidate.ImageUrl, e.Message);
            return null;
        }
    }

    private async Task<JsonDocument?> GetJsonAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync(url, ct);
            if (response is null || !response.IsSuccessStatusCode)
                return null;
            var data = await ReadCappedAsync(response, MaxJsonBytes, ct);
            return data is null ? null : JsonDocument.Parse(data);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogInformation("Picture lookup at {Host} failed: {Reason}", url.Host, e.Message);
            return null;
        }
    }

    /// <summary>GETs <paramref name="url"/>, following redirects only while they stay on the allowed hosts.</summary>
    private async Task<HttpResponseMessage?> SendAsync(Uri url, CancellationToken ct)
    {
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsAllowed(url))
                return null;

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
                return null;
            url = location.IsAbsoluteUri ? location : new Uri(url, location);
        }
        return null;
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool SameImage(Uri a, Uri b) => FileName(a) == FileName(b);

    /// <summary>The file a media URL shows, whatever thumbnail size it asks for.</summary>
    private static string FileName(Uri uri)
    {
        var last = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
        return ThumbPrefix().Replace(last, "");
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [GeneratedRegex(@"/\d+px-")]
    private static partial Regex ThumbWidth();

    [GeneratedRegex(@"^\d+px-")]
    private static partial Regex ThumbPrefix();
}
