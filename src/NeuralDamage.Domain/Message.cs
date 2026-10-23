namespace NeuralDamage.Domain;

public class Message : BaseEntity
{
    /// <summary>A story's snapshot is cut to this many characters.</summary>
    public const int MaxStoryTextLength = 500;

    public required Guid ChatId { get; init; }
    public Guid? SenderUserId { get; init; }
    public Guid? SenderBotId { get; init; }
    public required string Content { get; set; }
    public string? Mentions { get; set; }
    public Guid? ReplyToId { get; init; }

    /// <summary>A sticker message carries the sticker and no text.</summary>
    public Guid? StickerId { get; init; }

    /// <summary>
    /// A reply to a story: which one, whose, and what it said when the reply
    /// was sent. Stories expire, so this is a snapshot rather than a link; the
    /// story's picture travels as an attachment, for people only.
    /// </summary>
    public Guid? StoryId { get; init; }
    public Guid? StoryAuthorUserId { get; init; }
    public Guid? StoryAuthorBotId { get; init; }
    public string? StoryAuthorName { get; init; }
    public string? StoryText { get; init; }

    public bool IsStoryReply => StoryId is not null;

    public Chat Chat { get; set; } = null!;
    public User? SenderUser { get; set; }
    public Bot? SenderBot { get; set; }
    public Message? ReplyTo { get; set; }
    public Sticker? Sticker { get; set; }
    public ICollection<Reaction> Reactions { get; set; } = [];
    public ICollection<Attachment> Attachments { get; set; } = [];
}
