namespace NeuralDamage.Domain;

/// <summary>What a memory is about: the bot itself, one person, or a chat's own lore.</summary>
public enum MemoryScope
{
    /// <summary>The bot's own posts, opinions and running jokes. Read everywhere, the feed included.</summary>
    Self,

    /// <summary>Something about one person (<see cref="BotMemory.SubjectUserId"/>). Never on the feed.</summary>
    Person,

    /// <summary>Lore of one chat (<see cref="BotMemory.ChatId"/>), only read there.</summary>
    Chat,
}

/// <summary>Where a memory came from.</summary>
public enum MemorySource
{
    Chat,
    Feed,
    Story,

    /// <summary>Written by a person: /remember, or the bot's maker in its memory list.</summary>
    Manual,
}

/// <summary>
/// One thing a bot remembers, in a sentence. Each bot has its own: what one bot knows is never
/// read by another. A memory learned somewhere private (a direct chat, or by a bot private to its
/// chat) carries that chat in <see cref="ChatId"/> and <see cref="StaysInChat"/>, and is only read there.
/// </summary>
public class BotMemory : BaseEntity
{
    public const int MaxTextLength = 300;
    public const int MaxImportance = 10;

    public required Guid BotId { get; init; }
    public Bot Bot { get; set; } = null!;

    public MemoryScope Scope { get; init; }

    /// <summary>Who a <see cref="MemoryScope.Person"/> memory is about.</summary>
    public Guid? SubjectUserId { get; init; }
    public User? SubjectUser { get; set; }

    /// <summary>The chat it was learned in, if any.</summary>
    public Guid? ChatId { get; init; }

    /// <summary>Learned in private, so only read in <see cref="ChatId"/>.</summary>
    public bool StaysInChat { get; init; }

    public required string Text { get; set; }

    /// <summary>1 to <see cref="MaxImportance"/>: how much it matters, as the extractor or the person judged it.</summary>
    public int Importance { get; set; } = 5;

    public MemorySource Source { get; init; }

    /// <summary>Kept whatever happens, and always read where it applies.</summary>
    public bool Pinned { get; set; }

    /// <summary>When it last went into a prompt, for ranking and pruning.</summary>
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>How far a bot's memory has read a chat: messages before <see cref="ProcessedBefore"/> are done.</summary>
public class BotMemoryCursor : BaseEntity
{
    public required Guid BotId { get; init; }
    public required Guid ChatId { get; init; }
    public DateTime ProcessedBefore { get; set; }
}
