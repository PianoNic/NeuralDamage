namespace NeuralDamage.Application.Dtos.Requests;

public record SendMessageRequest(string Content, Guid? ReplyToId = null);
