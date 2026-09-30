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
}
