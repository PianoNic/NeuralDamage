namespace NeuralDamage.Domain;

/// <summary>
/// An image someone uploaded to a chat. It is uploaded before the message it
/// belongs to is sent, so <see cref="MessageId"/> stays null until then.
/// </summary>
public class Attachment : BaseEntity
{
    public required Guid ChatId { get; init; }
    public required Guid UploaderUserId { get; init; }
    public Guid? MessageId { get; set; }

    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }

    /// <summary>
    /// What the image shows, written once by the describer model so that bots
    /// on text-only models can follow along. Null until it has run.
    /// </summary>
    public string? Description { get; set; }

    public Chat Chat { get; set; } = null!;
    public User UploaderUser { get; set; } = null!;
    public Message? Message { get; set; }
}
