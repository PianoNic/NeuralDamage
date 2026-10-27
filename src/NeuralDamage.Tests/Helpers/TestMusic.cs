using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services.Music;

namespace NeuralDamage.Tests.Helpers;

/// <summary>Deezer without the network: songs it knows, a chart, and searches that answer from them.</summary>
public sealed class FakeDeezer : IDeezerClient
{
    public List<DeezerTrack> Tracks { get; } =
    [
        new(1, "Space Oddity", "David Bowie", "Space Oddity", "https://cdn.example/1.jpg", 318, "https://cdn.example/1.mp3"),
        new(2, "Rocket Man", "Elton John", "Honky Chateau", "https://cdn.example/2.jpg", 283, "https://cdn.example/2.mp3"),
        new(3, "Cornfield Chase", "Hans Zimmer", "Interstellar", "https://cdn.example/3.jpg", 126, "https://cdn.example/3.mp3"),
        new(4, "Fly Me to the Moon", "Frank Sinatra", null, null, 150, "https://cdn.example/4.mp3"),
    ];

    public List<long> Chart { get; } = [2, 3, 4];

    public List<string> Searches { get; } = [];

    public bool PreviewsFail { get; set; }

    public Task<IReadOnlyList<DeezerTrack>> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        Searches.Add(query);
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<DeezerTrack> found = Tracks
            .Where(t => words.Any(w => $"{t.Title} {t.Artist}".Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Take(limit).ToList();
        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<DeezerTrack>> ChartAsync(int limit = 20, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeezerTrack>>(Chart.Select(id => Tracks.Single(t => t.Id == id)).Take(limit).ToList());

    public Task<DeezerTrack?> TrackAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Tracks.FirstOrDefault(t => t.Id == id));

    public Task<byte[]?> PreviewAsync(DeezerTrack track, CancellationToken ct = default) =>
        Task.FromResult(PreviewsFail ? null : new byte[] { 0x49, 0x44, 0x33, (byte)track.Id });
}

/// <summary>A yt-dlp that "downloads" a small file, or finds nothing.</summary>
public sealed class FakeFullSongs(SongAudioStore store) : IFullSongFetcher
{
    public bool IsAvailable { get; set; } = true;
    public bool Finds { get; set; } = true;
    public int Calls { get; private set; }

    public Task<FullSong?> FetchAsync(string artist, string title, int durationSeconds, CancellationToken ct = default)
    {
        Calls++;
        if (!Finds)
            return Task.FromResult<FullSong?>(null);
        Directory.CreateDirectory(store.Root);
        var file = Path.Combine(store.Root, Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(file, [1, 2, 3]);
        return Task.FromResult<FullSong?>(new FullSong(file, "audio/mp4", durationSeconds + 1));
    }
}

public static class TestMusic
{
    public static SongAudioStore Store() =>
        new(Path.Combine(Path.GetTempPath(), "nd-music-tests", Guid.NewGuid().ToString("N")));

    public static SongLibrary Library(NeuralDamageDbContext db, IDeezerClient? deezer = null, SongAudioStore? store = null,
        SongFetchQueue? queue = null, IFullSongFetcher? fetcher = null, MusicOptions? options = null) =>
        new(db, deezer ?? new FakeDeezer(), store ?? Store(), Options.Create(options ?? new MusicOptions()),
            NullLogger<SongLibrary>.Instance, queue, fetcher);
}
