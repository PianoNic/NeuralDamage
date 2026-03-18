namespace NeuralDamage.Application.Dtos;

public record ReactionDto(Guid Id, Guid MessageId, Guid? UserId, Guid? BotId, string Emoji, DateTime CreatedAt);
