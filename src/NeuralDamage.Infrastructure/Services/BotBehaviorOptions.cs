namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// Knobs for how bots take part in a chat, bound from the "Bots" section.
/// </summary>
public class BotBehaviorOptions
{
    public const string SectionName = "Bots";

    /// <summary>
    /// Anti-spam: a bot that has already sent this many replies in the last
    /// minute sits the next message out, however it was addressed.
    /// </summary>
    public int MaxRepliesPerMinute { get; set; } = 4;

    /// <summary>
    /// At most this many bots answer any one message, chosen by how strongly
    /// they were addressed. Stops dogpiles and caps what one message can cost.
    /// </summary>
    public int MaxRespondersPerMessage { get; set; } = 2;

    /// <summary>A pause before typing starts, as if reading the message first.</summary>
    public TimeSpan ReadDelayMin { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan ReadDelayMax { get; set; } = TimeSpan.FromMilliseconds(2500);

    /// <summary>
    /// How fast a bot "types". A reply shows the typing indicator for about as
    /// long as a person would need to write it, within these bounds; time
    /// spent generating counts towards it.
    /// </summary>
    public double TypingCharsPerSecond { get; set; } = 12;
    public TimeSpan TypingMin { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan TypingMax { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// BotTyping is re-sent this often while a bot works; the client expires
    /// an indicator a few seconds after the last one. Zero sends it once.
    /// </summary>
    public TimeSpan TypingHeartbeat { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>A reply split on blank lines becomes at most this many messages.</summary>
    public int MaxReplyParts { get; set; } = 3;

    /// <summary>Reactions land a moment after the message, not the instant it arrives.</summary>
    public TimeSpan ReactionDelayMin { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan ReactionDelayMax { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>A random duration in [min, max].</summary>
    internal static TimeSpan Between(TimeSpan min, TimeSpan max) =>
        max <= min ? min : min + (max - min) * Random.Shared.NextDouble();

    /// <summary>How long a person would take to type <paramref name="length"/> characters.</summary>
    public TimeSpan TypingTime(int length)
    {
        var seconds = TypingCharsPerSecond > 0 ? length / TypingCharsPerSecond : 0;
        var typing = TimeSpan.FromSeconds(seconds);
        return typing < TypingMin ? TypingMin : typing > TypingMax ? TypingMax : typing;
    }
}
