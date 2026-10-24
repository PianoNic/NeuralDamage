namespace NeuralDamage.Infrastructure.Dtos.Requests;

/// <summary>Who to message: one bot or one person.</summary>
public record OpenDirectChatRequest(Guid? BotId, Guid? UserId);
