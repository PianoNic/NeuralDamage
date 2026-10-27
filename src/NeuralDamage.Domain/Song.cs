namespace NeuralDamage.Domain;

/// <summary>
/// A song from Deezer that a post or story plays: its title, artist and cover,
/// and its audio, stored once and shared by everything that uses it: Deezer's
/// preview, and the whole song when it could be fetched. Ones that nothing uses
/// any more are swept.
/// </summary>
public class Song : BaseEntity
{
    public const int MaxTitleLength = 300;

    /// <summary>A clip is this many seconds of the song.</summary>
    public const int ClipSeconds = 15;

    /// <summary>Deezer's id for the track, unique.</summary>
    public required long DeezerId { get; init; }

    public required string Title { get; set; }
    public required string Artist { get; set; }
    public string? Album { get; set; }

    /// <summary>The album cover on Deezer's image CDN.</summary>
    public string? CoverUrl { get; set; }

    /// <summary>The whole song's length.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Deezer's 30-second preview (an MP3) is stored; a post without a clip start plays it.</summary>
    public bool HasPreview { get; set; }

    /// <summary>The whole song's audio type, like <c>audio/mp4</c>, once it is stored; then any part of it can be the clip.</summary>
    public string? FullContentType { get; set; }

    /// <summary>The stored whole song's length.</summary>
    public int FullSeconds { get; set; }

    public bool HasFull => FullContentType is not null;

    /// <summary>When the last try at the whole song failed, so it isn't tried again on every use.</summary>
    public DateTime? FullFailedAt { get; set; }

    /// <summary>Where a clip may start in the whole song, kept so all of it fits.</summary>
    public int ClampStart(int start) => Math.Clamp(start, 0, Math.Max(0, FullSeconds - ClipSeconds));
}
