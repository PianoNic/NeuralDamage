namespace NeuralDamage.Domain;

public class Chat : BaseEntity
{
    public required string Name { get; set; }

    public required Guid CreatedById { get; init; }
    public User CreatedBy { get; set; } = null!;

    public ICollection<ChatMember> Members { get; set; } = [];
    public ICollection<Message> Messages { get; set; } = [];
}
