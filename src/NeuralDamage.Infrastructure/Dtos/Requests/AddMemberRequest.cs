namespace NeuralDamage.Infrastructure.Dtos.Requests;

public record AddMemberRequest(Guid? UserId = null, Guid? BotId = null);
