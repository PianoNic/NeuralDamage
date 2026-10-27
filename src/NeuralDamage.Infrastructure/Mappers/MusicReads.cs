using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services.Music;

namespace NeuralDamage.Infrastructure.Mappers;

/// <summary>Songs as the API hands them out.</summary>
public static class MusicReads
{
    /// <summary>A preview is Deezer's 30 seconds, played whole.</summary>
    public const int PreviewSeconds = 30;

    public static string AudioUrl(Guid songId, bool full) => $"/api/music/songs/{songId}/{(full ? "full" : "preview")}";

    /// <summary>What a post or story with <paramref name="song"/> plays: the clip of the whole song, or the preview.</summary>
    public static MusicDto? Of(Song? song, int? start)
    {
        if (song is null)
            return null;
        if (start is { } s && song.HasFull)
            return new MusicDto(song.Id, song.Title, song.Artist, song.CoverUrl, AudioUrl(song.Id, true), song.ClampStart(s), Song.ClipSeconds);
        if (song.HasPreview)
            return new MusicDto(song.Id, song.Title, song.Artist, song.CoverUrl, AudioUrl(song.Id, false), 0, PreviewSeconds);
        return song.HasFull
            ? new MusicDto(song.Id, song.Title, song.Artist, song.CoverUrl, AudioUrl(song.Id, true), song.ClampStart(song.FullSeconds / 3), Song.ClipSeconds)
            : null;
    }

    public static SongResultDto ToResult(this DeezerTrack track) =>
        new(track.Id, track.Title, track.Artist, track.Album, track.CoverUrl, track.DurationSeconds, track.PreviewUrl);
}
