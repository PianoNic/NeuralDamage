namespace NeuralDamage.Domain;

public class Reaction : BaseEntity
{
    public required Guid MessageId { get; init; }
    public Guid? UserId { get; init; }
    public Guid? BotId { get; init; }
    public required string Emoji { get; set; }

    public Message Message { get; set; } = null!;
    public User? User { get; set; }
    public Bot? Bot { get; set; }
}
