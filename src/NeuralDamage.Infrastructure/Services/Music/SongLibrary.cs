using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;

namespace NeuralDamage.Infrastructure.Services.Music;

/// <summary>
/// The songs the app has stored. A song is added the first time a post or story uses it,
/// with Deezer's preview at once; the whole song follows from <see cref="SongFetchQueue"/>
/// when someone wants to pick their own clip.
/// </summary>
public class SongLibrary(
    NeuralDamageDbContext db,
    IDeezerClient deezer,
    SongAudioStore store,
    IOptions<MusicOptions> options,
    ILogger<SongLibrary> logger,
    SongFetchQueue? queue = null,
    IFullSongFetcher? fetcher = null)
{
    private readonly MusicOptions _options = options.Value;

    /// <summary>Whether whole songs can be fetched, so a clip can start anywhere.</summary>
    public bool CanFetchFull => fetcher is { IsAvailable: true };

    /// <summary>
    /// The song with Deezer's <paramref name="deezerId"/>, stored with its preview; null when Deezer
    /// doesn't know it or none of its audio could be fetched.
    /// </summary>
    /// <param name="wantFull">Also fetch the whole song, in the background, when it isn't stored yet.</param>
    public async Task<Song?> AddAsync(long deezerId, bool wantFull, CancellationToken ct = default)
    {
        var song = await db.Songs.FirstOrDefaultAsync(s => s.DeezerId == deezerId, ct);
        DeezerTrack? track = null;
        if (song is null)
        {
            track = await deezer.TrackAsync(deezerId, ct);
            if (track is null)
                return null;
            song = new Song
            {
                DeezerId = track.Id,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                CoverUrl = track.CoverUrl,
                DurationSeconds = track.DurationSeconds,
            };
            db.Songs.Add(song);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Added twice at once: the other one is the song.
                db.ChangeTracker.Clear();
                song = await db.Songs.FirstOrDefaultAsync(s => s.DeezerId == deezerId, ct);
                if (song is null)
                    return null;
            }
        }

        if (!song.HasPreview)
        {
            track ??= await deezer.TrackAsync(deezerId, ct);
            if (track is not null && await deezer.PreviewAsync(track, ct) is { } preview)
            {
                await store.SavePreviewAsync(song.Id, preview, ct);
                song.HasPreview = true;
                song.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        if (!song.HasPreview && !song.HasFull)
        {
            logger.LogWarning("Song {Title} by {Artist} has no audio to play", song.Title, song.Artist);
            return null;
        }

        if (wantFull && ShouldFetchFull(song, DateTime.UtcNow))
            queue?.Request(song.Id);
        return song;
    }

    /// <summary>Whether to try fetching the whole song: it isn't stored, and the last try isn't too recent.</summary>
    public bool ShouldFetchFull(Song song, DateTime now) =>
        CanFetchFull && !song.HasFull && (song.FullFailedAt is not { } failed || now - failed >= _options.FullRetryAfter);

    /// <summary>Fetches and keeps the whole song; false when it wasn't found.</summary>
    public async Task<bool> FetchFullAsync(Guid songId, CancellationToken ct = default)
    {
        var song = await db.Songs.FirstOrDefaultAsync(s => s.Id == songId, ct);
        if (song is null || song.HasFull || fetcher is null)
            return song?.HasFull ?? false;

        var full = await fetcher.FetchAsync(song.Artist, song.Title, song.DurationSeconds, ct);
        if (full is null)
        {
            song.FullFailedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return false;
        }

        store.SaveFull(song.Id, full.File);
        song.FullContentType = full.ContentType;
        song.FullSeconds = full.Seconds > 0 ? full.Seconds : song.DurationSeconds;
        song.FullFailedAt = null;
        song.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Stored the whole song {Title} by {Artist}", song.Title, song.Artist);
        return true;
    }

    /// <summary>
    /// Deletes the songs nothing uses any more, audio and all: on no post and no live story, and
    /// older than <see cref="MusicOptions.UnusedLifetime"/>, so one just picked isn't swept before it is posted.
    /// </summary>
    public async Task<int> SweepAsync(DateTime now, CancellationToken ct = default)
    {
        var before = now - _options.UnusedLifetime;
        var unused = await db.Songs
            .Where(s => s.UpdatedAt < before
                && !db.Posts.Any(p => p.SongId == s.Id)
                && !db.Stories.Any(st => st.SongId == s.Id && st.ExpiresAt > now))
            .ToListAsync(ct);
        foreach (var song in unused)
            store.Delete(song.Id);
        db.Songs.RemoveRange(unused);
        await db.SaveChangesAsync(ct);
        return unused.Count;
    }

    /// <summary>The song for the picker: what it is, and its audio so far.</summary>
    public SongDto ToDto(Song song) => new(
        song.Id, song.DeezerId, song.Title, song.Artist, song.Album, song.CoverUrl, song.DurationSeconds,
        song.HasPreview ? MusicReads.AudioUrl(song.Id, full: false) : null,
        song.HasFull ? MusicReads.AudioUrl(song.Id, full: true) : null,
        song.FullSeconds,
        !song.HasFull && queue?.IsPending(song.Id) == true);

    /// <summary>The song a post or story plays, checked: a stored one, with its clip start kept inside it.</summary>
    /// <param name="start">Where the clip starts in the whole song; ignored, and the preview played, when it isn't stored.</param>
    public async Task<(Guid SongId, int? Start)?> UseAsync(Guid songId, int? start, CancellationToken ct = default)
    {
        var song = await db.Songs.FirstOrDefaultAsync(s => s.Id == songId, ct);
        if (song is null || (!song.HasPreview && !song.HasFull))
            return null;
        // Used again, so the sweep leaves it.
        song.UpdatedAt = DateTime.UtcNow;
        var clip = start is { } s && song.HasFull ? song.ClampStart(s) : (int?)null;
        if (clip is null && !song.HasPreview)
            clip = song.ClampStart(song.FullSeconds / 3);
        return (song.Id, clip);
    }
}
