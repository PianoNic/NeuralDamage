namespace NeuralDamage.Domain;

public enum GeneratedImageStatus
{
    Pending,
    Ready,
    Failed,
}

/// <summary>
/// A picture the image generator made, asked for by a person (for a post they
/// are writing) or by a bot (for its own post). It is stored like an upload
/// and belongs to nothing until a post takes it; unused ones are swept.
/// </summary>
public class GeneratedImage : BaseEntity
{
    public const int MaxPromptLength = 1000;

    /// <summary>The person who asked for it; null when a bot did.</summary>
    public Guid? UserId { get; init; }

    /// <summary>The bot that asked for it; null when a person did.</summary>
    public Guid? BotId { get; init; }

    /// <summary>The bot whose face is in it (its profile picture was the reference); null for a plain scene.</summary>
    public Guid? FaceBotId { get; set; }

    public required string Prompt { get; init; }

    public GeneratedImageStatus Status { get; set; } = GeneratedImageStatus.Pending;

    /// <summary>Why it failed, in a few words, for the person who asked.</summary>
    public string? Error { get; set; }

    public string ContentType { get; set; } = "image/png";
    public long SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}
