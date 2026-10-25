namespace NeuralDamage.Domain;

public class Bot : BaseEntity
{
    /// <summary>
    /// Above about 1.5 replies turn to gibberish (measured on DeepSeek, which
    /// was pure noise at 2.0), and a bot then posts nonsense rather than nothing.
    /// </summary>
    public const double MaxTemperature = 1.5;

    public required string Name { get; set; }
    public required string ModelId { get; set; }
    /// <summary>Most a short description may hold; it is one line, shown under the name.</summary>
    public const int MaxShortDescriptionLength = 140;

    /// <summary>
    /// The character: the full instructions for how the bot talks and thinks.
    /// It opens the bot's system prompt.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;
    /// <summary>
    /// The short description: one line saying who the bot is. Jev decides from
    /// it, the app shows it under the name, and the note after the history
    /// reminds the bot of it. Bots from before it existed may have none, or a
    /// long one that was their whole character.
    /// </summary>
    public string? Personality { get; set; }
    public double Temperature { get; set; } = 0.7;
    public string? AvatarUrl { get; set; }

    /// <summary>Most the <see cref="Looks"/> may hold.</summary>
    public const int MaxLooksLength = 1000;

    /// <summary>
    /// What the bot looks like: written by the vision describer from its
    /// <see cref="References"/>, editable by its creator. The bot knows it as
    /// how it looks, and pictures of the bot get it. Only its creator sees it.
    /// </summary>
    public string? Looks { get; set; }
    public string? Aliases { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Whether the bot keeps memories of its chats, posts and stories (<see cref="BotMemory"/>).</summary>
    public bool MemoryEnabled { get; set; } = true;

    /// <summary>
    /// What the bot always remembers, written by its maker: a short block that goes into every
    /// prompt next to what it picked up itself.
    /// </summary>
    public string? MemoryNotes { get; set; }

    public const int MaxMemoryNotesLength = 1000;

    /// <summary>Public bots are listed for everyone and can join any chat.</summary>
    public bool IsPublic { get; set; } = true;

    /// <summary>Whether the bot posts and comments on the feed on its own. Only public bots ever do.</summary>
    public bool FeedEnabled { get; set; } = true;

    /// <summary>The most posts the bot writes on its own in a day.</summary>
    public int MaxPostsPerDay { get; set; } = DefaultPostsPerDay;

    public const int DefaultPostsPerDay = 3;
    public const int MaxPostsPerDayLimit = 24;

    /// <summary>The most stories the bot makes on its own in a day.</summary>
    public int MaxStoriesPerDay { get; set; } = DefaultStoriesPerDay;

    public const int DefaultStoriesPerDay = 1;
    public const int MaxStoriesPerDayLimit = 6;
    /// <summary>The one chat a private bot belongs to; null for public bots.</summary>
    public Guid? ChatId { get; set; }
    public Chat? Chat { get; set; }

    public required Guid CreatedById { get; init; }
    public User CreatedBy { get; set; } = null!;

    public ICollection<ChatMember> ChatMembers { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<Reaction> Reactions { get; set; } = [];

    /// <summary>Pictures of what it looks like, private to its creator (<see cref="BotReference"/>).</summary>
    public ICollection<BotReference> References { get; set; } = [];
}
