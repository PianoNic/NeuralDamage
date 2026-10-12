namespace NeuralDamage.Domain;

public class Message : BaseEntity
{
    public required Guid ChatId { get; init; }
    public Guid? SenderUserId { get; init; }
    public Guid? SenderBotId { get; init; }
    public required string Content { get; set; }
    public string? Mentions { get; set; }
    public Guid? ReplyToId { get; init; }

    /// <summary>A sticker message carries the sticker and no text.</summary>
    public Guid? StickerId { get; init; }

    public Chat Chat { get; set; } = null!;
    public User? SenderUser { get; set; }
    public Bot? SenderBot { get; set; }
    public Message? ReplyTo { get; set; }
    public Sticker? Sticker { get; set; }
    public ICollection<Reaction> Reactions { get; set; } = [];
    public ICollection<Attachment> Attachments { get; set; } = [];
}
