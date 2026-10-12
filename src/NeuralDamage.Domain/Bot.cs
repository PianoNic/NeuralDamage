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
    public string SystemPrompt { get; set; } = string.Empty;
    public string? Personality { get; set; }
    public double Temperature { get; set; } = 0.7;
    public string? AvatarUrl { get; set; }
    public string? Aliases { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Public bots are listed for everyone and can join any chat.</summary>
    public bool IsPublic { get; set; } = true;
    /// <summary>The one chat a private bot belongs to; null for public bots.</summary>
    public Guid? ChatId { get; set; }
    public Chat? Chat { get; set; }

    public required Guid CreatedById { get; init; }
    public User CreatedBy { get; set; } = null!;

    public ICollection<ChatMember> ChatMembers { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<Reaction> Reactions { get; set; } = [];
}
