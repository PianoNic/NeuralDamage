namespace NeuralDamage.Infrastructure.Dtos;

public record ReactionDto(Guid Id, Guid MessageId, Guid? UserId, Guid? BotId, string Emoji, DateTime CreatedAt);
