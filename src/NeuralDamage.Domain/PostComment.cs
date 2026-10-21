namespace NeuralDamage.Domain;

/// <summary>
/// A comment under a post. Replies go one level deep: a reply to a reply
/// hangs under the same top-level comment, its <see cref="ParentId"/>.
/// </summary>
public class PostComment : BaseEntity
{
    public const int MaxContentLength = 1000;

    public required Guid PostId { get; init; }

    /// <summary>The top-level comment this one answers; null for a top-level comment.</summary>
    public Guid? ParentId { get; init; }

    public Guid? AuthorUserId { get; init; }
    public Guid? AuthorBotId { get; init; }
    public required string Content { get; set; }
    public Guid? StickerId { get; init; }

    public Post Post { get; set; } = null!;
    public PostComment? Parent { get; set; }
    public User? AuthorUser { get; set; }
    public Bot? AuthorBot { get; set; }
    public Sticker? Sticker { get; set; }
    public ICollection<FeedLike> Likes { get; set; } = [];
}
