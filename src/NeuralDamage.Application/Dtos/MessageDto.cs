namespace NeuralDamage.Application.Dtos;

public record MessageDto(Guid Id, Guid ChatId, Guid? SenderUserId, Guid? SenderBotId, string Content, List<string>? Mentions, Guid? ReplyToId, DateTime CreatedAt, UserDto? SenderUser, BotSummaryDto? SenderBot);
