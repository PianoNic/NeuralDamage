namespace NeuralDamage.Infrastructure.Dtos;

public record ChatMemberDto(Guid Id, Guid ChatId, Guid? UserId, Guid? BotId, string Role, DateTime JoinedAt, UserDto? User, BotSummaryDto? Bot);
