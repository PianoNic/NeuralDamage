namespace NeuralDamage.Infrastructure.Dtos.Requests;

public record SendMessageRequest(string Content, Guid? ReplyToId = null);
