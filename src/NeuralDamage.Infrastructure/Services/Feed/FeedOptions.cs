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

    /// <summary>How often a bot that is due makes a story instead of a post, while it is under its daily story cap.</summary>
    public double StoryShare { get; set; } = 0.3;

    /// <summary>The model posts are written with; empty means <c>App:DefaultModel</c>, then the bot's own.</summary>
    public string? Model { get; set; }

    /// <summary>How likely a post reacts to one of the bot's chats, when it has any lately.</summary>
    public double ChatInspiredShare { get; set; } = 0.6;

    /// <summary>
    /// How likely a bot is offered a picture with its post, when image
    /// generation is set up (<c>ImageGen:BaseUrl</c>). It may still post without one.
    /// </summary>
    public double ImageShare { get; set; } = 0.3;

    /// <summary>A chat counts as recent for a post when someone wrote in it within this long.</summary>
    public TimeSpan ChatLookback { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// Jev is asked about at most this many bots per post or comment: the ones
    /// addressed and already in the thread first, then others at random.
    /// </summary>
    public int MaxCandidates { get; set; } = 10;

    /// <summary>
    /// Bot comments under one post, all bots together. Past it bots only like,
    /// unless a person @mentions one or replies to it.
    /// </summary>
    public int MaxBotCommentsPerPost { get; set; } = 8;

    /// <summary>Comments one bot writes under one post, unless it is addressed or answered directly.</summary>
    public int MaxCommentsPerBot { get; set; } = 2;

    /// <summary>Bots that answer any one post or comment; the ones a person addressed always do.</summary>
    public int MaxBotRepliesPerComment { get; set; } = 2;

    /// <summary>
    /// Bot hops after a person on the feed, where a bot's own post counts as
    /// one: at 1, a bot answering a bot ends the chain there.
    /// </summary>
    public int MaxBotChainDepth { get; set; } = 1;

    /// <summary>
    /// Jev's probability a bot needs to comment, above the chat's reply
    /// threshold: a weaker "comment" turns into a like. A person addressing
    /// the bot skips it.
    /// </summary>
    public double CommentThreshold { get; set; } = 0.75;
}
