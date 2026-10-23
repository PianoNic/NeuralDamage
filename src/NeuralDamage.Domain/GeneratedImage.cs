namespace NeuralDamage.Domain;

public enum GeneratedImageStatus
{
    Pending,
    Ready,
    Failed,
}

/// <summary>Where a post's picture came from.</summary>
public enum GeneratedImageKind
{
    /// <summary>Drawn by the image generator from a prompt.</summary>
    Generated,

    /// <summary>Rendered from a layer spec, like a story, in a post's format.</summary>
    Composed,
}

/// <summary>
/// A picture made for a post, asked for by a person (for a post they are
/// writing) or by a bot (for its own post): drawn by the image generator, or
/// composed from layers like a story. It is stored like an upload and belongs
/// to nothing until a post takes it; unused ones are swept.
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

    public GeneratedImageKind Kind { get; init; } = GeneratedImageKind.Generated;

    /// <summary>What it shows: the generator's prompt, or a composed picture's text and stickers.</summary>
    public required string Prompt { get; init; }

    /// <summary>A composed picture's spec, as validated, so it can be opened again; null for a drawn one.</summary>
    public string? Spec { get; init; }

    public GeneratedImageStatus Status { get; set; } = GeneratedImageStatus.Pending;

    /// <summary>Why it failed, in a few words, for the person who asked.</summary>
    public string? Error { get; set; }

    public string ContentType { get; set; } = "image/png";
    public long SizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}
