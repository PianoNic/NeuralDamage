namespace NeuralDamage.Domain;

public class Chat : BaseEntity
{
    public required string Name { get; set; }

    public required Guid CreatedById { get; init; }

    /// <summary>
    /// Set on a direct chat: the two members' ids, in order, so there is only ever one per pair. A
    /// direct chat keeps its two members and takes its name from the other one.
    /// </summary>
    public string? DirectKey { get; init; }
    public bool IsDirect => DirectKey is not null;

    /// <summary>A group chat's own picture; a direct chat shows the other one's.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>The key of the direct chat between a person and a bot or another person.</summary>
    public static string DirectKeyOf(Guid a, Guid b)
    {
        string first = a.ToString(), second = b.ToString();
        return string.CompareOrdinal(first, second) < 0 ? $"{first}:{second}" : $"{second}:{first}";
    }
    public User CreatedBy { get; set; } = null!;

    /// <summary>
    /// A running summary of the messages that dropped out of the bots' history
    /// window, so they still remember them. Shared by every bot in the chat.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>Every message sent before this is in <see cref="Summary"/>.</summary>
    public DateTime? SummarizedBefore { get; set; }

    public ICollection<ChatMember> Members { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
}
