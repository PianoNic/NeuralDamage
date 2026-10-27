namespace NeuralDamage.Domain;

public class User : BaseEntity
{
    public required string ExternalId { get; init; }
    public required string Email { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Whether the person may do anything any owner can. Set at every sign-in sync from
    /// <c>App:Admins</c> and the admin role their token carries.
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>When the user last opened the feed; what is newer counts as unread.</summary>
    public DateTime? FeedSeenAt { get; set; }

    /// <summary>
    /// Comma-separated names the person also goes by, like a bot's aliases: "@Niclas" mentions
    /// PianoNic when Niclas is one, and bots are told them next to the handle.
    /// </summary>
    public string? Nicknames { get; set; }

    /// <summary>Whether bots may keep memories about the person; off drops what they had too.</summary>
    public bool AllowBotMemory { get; set; } = true;

    /// <summary>Whether bots in the person's group chats may decide to text them directly.</summary>
    public bool AllowBotDirectMessages { get; set; } = true;

    public ICollection<Bot> CreatedBots { get; set; } = [];
    public ICollection<Chat> CreatedChats { get; set; } = [];
    public ICollection<ChatMember> ChatMembers { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<Reaction> Reactions { get; set; } = [];
}
