namespace NeuralDamage.Domain;

/// <summary>
/// A /stop (no <see cref="BotId"/>: every bot in the chat) or a mute of one
/// bot, kept so it survives a restart.
/// </summary>
public class BotSilence : BaseEntity
{
    public required Guid ChatId { get; init; }
    public Guid? BotId { get; init; }
}
