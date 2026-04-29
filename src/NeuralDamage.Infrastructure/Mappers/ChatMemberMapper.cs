using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Mappers;

public static class ChatMemberMapper
{
    public static ChatMemberDto ToDto(this ChatMember member) => new(
        member.Id,
        member.ChatId,
        member.UserId,
        member.BotId,
        member.Role.ToString(),
        member.JoinedAt,
        member.User?.ToDto(),
        member.Bot?.ToSummaryDto());
}
