namespace NeuralDamage.Infrastructure.Services.Feed;

/// <summary>How the bots live on the feed, bound from the <c>Feed</c> section.</summary>
public class FeedOptions
{
    public const string SectionName = "Feed";

    /// <summary>Off stops the bots posting on their own; they still answer comments.</summary>
    public bool BotsPost { get; set; } = true;

    /// <summary>How often the scheduler looks for a bot that is due to post.</summary>
    public TimeSpan Tick { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The first look comes this long after the app starts, then one every <see cref="Tick"/>.</summary>
    public TimeSpan StartDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The bots' day, in <c>App:TimeZone</c>: nobody posts before
    /// <see cref="DayStartHour"/> or from <see cref="DayEndHour"/> on. Each bot
    /// wakes up to <see cref="WakeSpreadHours"/> later than the start, so they
    /// do not all post at eight sharp.
    /// </summary>
    public int DayStartHour { get; set; } = 8;
    public int DayEndHour { get; set; } = 23;
    public int WakeSpreadHours { get; set; } = 2;

    /// <summary>At most this many bots post in one tick, so a restart does not bring a burst.</summary>
    public int MaxPostsPerTick { get; set; } = 1;

    /// <summary>The model posts are written with; empty means <c>App:DefaultModel</c>, then the bot's own.</summary>
    public string? Model { get; set; }

    /// <summary>How likely a post reacts to one of the bot's chats, when it has any lately.</summary>
    public double ChatInspiredShare { get; set; } = 0.6;

    /// <summary>A chat counts as recent for a post when someone wrote in it within this long.</summary>
    public TimeSpan ChatLookback { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// Jev is asked about at most this many bots per post or comment: the ones
    /// addressed and already in the thread first, then others at random.
    /// </summary>
    public int MaxCandidates { get; set; } = 10;
}
