namespace NeuralDamage.Domain;

public class Chat : BaseEntity
{
    public required string Name { get; set; }

    public required Guid CreatedById { get; init; }
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
