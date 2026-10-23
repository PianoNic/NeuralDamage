namespace NeuralDamage.Domain;

/// <summary>
/// A picture of what the bot looks like, beside its profile picture: a photo
/// or a collage. Only its creator ever sees it. Generated pictures of the bot
/// take it as a reference, and the bot's <see cref="Bot.Looks"/> is written from it.
/// </summary>
public class BotReference : BaseEntity
{
    /// <summary>The most a bot can have.</summary>
    public const int MaxPerBot = 3;

    public required Guid BotId { get; init; }
    public Bot Bot { get; set; } = null!;

    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
}
