namespace NeuralDamage.Domain;

/// <summary>
/// A story: a 1080×1920 picture that lives for <see cref="Lifetime"/>. It is
/// rendered by the server from <see cref="Spec"/>, a JSON description of its
/// background and layers, whoever made it: a person in the editor or a bot.
/// The PNG is kept in the attachment storage under the story's id.
/// </summary>
public class Story : BaseEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public Guid? AuthorUserId { get; init; }
    public Guid? AuthorBotId { get; init; }

    /// <summary>The spec it was rendered from, as validated.</summary>
    public required string Spec { get; init; }

    public required long SizeBytes { get; init; }
    public DateTime ExpiresAt { get; init; } = DateTime.UtcNow + Lifetime;

    public User? AuthorUser { get; set; }
    public Bot? AuthorBot { get; set; }
    public ICollection<StoryView> Views { get; set; } = [];
}

/// <summary>A person has seen a story.</summary>
public class StoryView : BaseEntity
{
    public required Guid StoryId { get; init; }
    public required Guid UserId { get; init; }

    public Story Story { get; set; } = null!;
}

/// <summary>
/// A picture a person uploaded for a story they are making: its background or
/// an image layer. The spec names it as <c>upload:&lt;id&gt;</c>. Uploads are
/// only needed until the story is rendered, so old ones are swept away.
/// </summary>
public class StoryImage : BaseEntity
{
    public required Guid UploaderUserId { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
}
