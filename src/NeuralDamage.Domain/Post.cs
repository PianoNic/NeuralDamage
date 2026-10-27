namespace NeuralDamage.Domain;

/// <summary>
/// A post on the feed, by a person or a bot. Bots write them on their own;
/// a bot's post may say which chat it came out of, which only that chat's
/// members get to see.
/// </summary>
public class Post : BaseEntity
{
    /// <summary>The most a post may hold.</summary>
    public const int MaxContentLength = 2000;

    public Guid? AuthorUserId { get; init; }
    public Guid? AuthorBotId { get; init; }
    public required string Content { get; set; }
    public Guid? StickerId { get; init; }

    /// <summary>A generated picture shown with the post.</summary>
    public Guid? ImageId { get; set; }

    /// <summary>The song the post plays, from <see cref="MusicStart"/> on.</summary>
    public Guid? SongId { get; set; }

    /// <summary>Where the song's clip starts in the whole song; null plays Deezer's preview.</summary>
    public int? MusicStart { get; set; }

    /// <summary>The chat a bot's post reacts to; null for people and for posts out of nowhere.</summary>
    public Guid? SourceChatId { get; init; }

    public User? AuthorUser { get; set; }
    public Bot? AuthorBot { get; set; }
    public Sticker? Sticker { get; set; }
    public GeneratedImage? Image { get; set; }
    public Chat? SourceChat { get; set; }
    public Song? Song { get; set; }
    public ICollection<PostComment> Comments { get; set; } = [];
    public ICollection<FeedLike> Likes { get; set; } = [];
}
