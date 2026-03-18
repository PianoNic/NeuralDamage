using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Domain;

public class ChatMember : BaseEntity
{
    public required Guid ChatId { get; init; }
    public Guid? UserId { get; init; }
    public Guid? BotId { get; init; }
    public ChatMemberRole Role { get; set; } = ChatMemberRole.Member;
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;

    public Chat Chat { get; set; } = null!;
    public User? User { get; set; }
    public Bot? Bot { get; set; }
}
