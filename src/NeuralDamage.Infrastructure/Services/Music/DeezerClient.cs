using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NeuralDamage.Infrastructure.Services.Music;

/// <summary>A song as Deezer has it.</summary>
/// <param name="PreviewUrl">Its 30-second preview; the link stops working after a while, so it is fetched when needed.</param>
public record DeezerTrack(long Id, string Title, string Artist, string? Album, string? CoverUrl, int DurationSeconds, string? PreviewUrl);

public interface IDeezerClient
{
    /// <summary>Songs for <paramref name="query"/>, best match first; none when the search fails.</summary>
    Task<IReadOnlyList<DeezerTrack>> SearchAsync(string query, int limit = 20, CancellationToken ct = default);

    /// <summary>What is popular right now; none when Deezer can't be reached.</summary>
    Task<IReadOnlyList<DeezerTrack>> ChartAsync(int limit = 20, CancellationToken ct = default);

    /// <summary>One song, with a fresh preview link; null when Deezer doesn't know it.</summary>
    Task<DeezerTrack?> TrackAsync(long id, CancellationToken ct = default);

    /// <summary>The preview's MP3; null when it can't be fetched.</summary>
    Task<byte[]?> PreviewAsync(DeezerTrack track, CancellationToken ct = default);
}

/// <summary>Deezer's public API (<c>/search</c>, <c>/chart</c>, <c>/track</c>), which needs no key.</summary>
public class DeezerClient(HttpClient http, IOptions<MusicOptions> options, ILogger<DeezerClient> logger) : IDeezerClient
{
    public const int MaxQueryLength = 120;
    private const int MaxPreviewBytes = 4 * 1024 * 1024;

    private readonly MusicOptions _options = options.Value;

    public async Task<IReadOnlyList<DeezerTrack>> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0)
            return [];
        if (query.Length > MaxQueryLength)
            query = query[..MaxQueryLength];
        return await ListAsync($"search?q={Uri.EscapeDataString(query)}&limit={limit}", ct);
    }

    public Task<IReadOnlyList<DeezerTrack>> ChartAsync(int limit = 20, CancellationToken ct = default) =>
        ListAsync($"chart/0/tracks?limit={limit}", ct);

    public async Task<DeezerTrack?> TrackAsync(long id, CancellationToken ct = default)
    {
        using var doc = await GetAsync($"track/{id}", ct);
        return doc is null || doc.RootElement.TryGetProperty("error", out _) ? null : Track(doc.RootElement);
    }

    public async Task<byte[]?> PreviewAsync(DeezerTrack track, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(track.PreviewUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            return null;
        try
        {
            using var timeout = Timeout(ct);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxPreviewBytes)
                return null;
            var data = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            return data.Length is > 0 and <= MaxPreviewBytes ? data : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "Could not fetch the preview of Deezer track {TrackId}", track.Id);
            return null;
        }
    }

    private async Task<IReadOnlyList<DeezerTrack>> ListAsync(string path, CancellationToken ct)
    {
        using var doc = await GetAsync(path, ct);
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];
        return data.EnumerateArray().Select(Track).OfType<DeezerTrack>().ToList();
    }

    private async Task<JsonDocument?> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            using var timeout = Timeout(ct);
            using var response = await http.GetAsync(new Uri(new Uri(_options.DeezerBaseUrl), path), timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Deezer answered {Status} to {Path}", (int)response.StatusCode, path);
                return null;
            }
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "Deezer request {Path} failed", path);
            return null;
        }
    }

    private CancellationTokenSource Timeout(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(_options.DeezerTimeout);
        return source;
    }

    /// <summary>A track object; null when it lacks an id, title or artist, or can't be played.</summary>
    public static DeezerTrack? Track(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object
            || !e.TryGetProperty("id", out var id) || !id.TryGetInt64(out var trackId)
            || Text(e, "title") is not { } title
            || !e.TryGetProperty("artist", out var artist) || Text(artist, "name") is not { } artistName)
            return null;
        if (e.TryGetProperty("readable", out var readable) && readable.ValueKind == JsonValueKind.False)
            return null;

        var album = e.TryGetProperty("album", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
        var duration = e.TryGetProperty("duration", out var d) && d.TryGetInt32(out var seconds) ? seconds : 0;
        return new DeezerTrack(
            trackId,
            Cut(Text(e, "title_short") ?? title),
            Cut(artistName),
            album.ValueKind == JsonValueKind.Object && Text(album, "title") is { } albumTitle ? Cut(albumTitle) : null,
            album.ValueKind == JsonValueKind.Object ? Text(album, "cover_big") ?? Text(album, "cover_medium") : null,
            duration,
            Text(e, "preview"));
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static string Cut(string text) => text.Length > Domain.Song.MaxTitleLength ? text[..Domain.Song.MaxTitleLength] : text;
}
