namespace NeuralDamage.Infrastructure.Services.Music;

/// <summary>Music on posts and stories, bound from the <c>Music</c> section.</summary>
public class MusicOptions
{
    public const string SectionName = "Music";

    /// <summary>Off hides the music picker, and bots stop adding songs. Posts that have one keep playing it.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Deezer's public API; no key needed.</summary>
    public string DeezerBaseUrl { get; set; } = "https://api.deezer.com/";

    /// <summary>How long one Deezer request may take.</summary>
    public TimeSpan DeezerTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// The yt-dlp executable that fetches whole songs, so any part of one can be the clip. Unset, or a
    /// path that isn't there, keeps every song to Deezer's 30-second preview. The Docker image has it.
    /// </summary>
    public string? YtDlpPath { get; set; } = "yt-dlp";

    /// <summary>How long fetching one whole song may take.</summary>
    public TimeSpan YtDlpTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>A whole song bigger than this isn't kept.</summary>
    public int MaxAudioMegabytes { get; set; } = 25;

    /// <summary>After a failed try at a whole song, it is tried again only this much later.</summary>
    public TimeSpan FullRetryAfter { get; set; } = TimeSpan.FromDays(1);

    /// <summary>How often a bot's post or story comes with a song.</summary>
    public double BotShare { get; set; } = 0.35;

    /// <summary>How many songs a bot is offered to pick from.</summary>
    public int ShortlistSize { get; set; } = 6;

    /// <summary>A song nothing uses is deleted, audio and all, once it is this old.</summary>
    public TimeSpan UnusedLifetime { get; set; } = TimeSpan.FromDays(1);
}
