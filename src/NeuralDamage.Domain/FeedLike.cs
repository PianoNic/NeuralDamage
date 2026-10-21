namespace NeuralDamage.Domain;

/// <summary>A like on a post (no <see cref="CommentId"/>) or on one of its comments, by a person or a bot.</summary>
public class FeedLike : BaseEntity
{
    public required Guid PostId { get; init; }
    public Guid? CommentId { get; init; }
    public Guid? UserId { get; init; }
    public Guid? BotId { get; init; }

    public Post Post { get; set; } = null!;
    public PostComment? Comment { get; set; }
}
