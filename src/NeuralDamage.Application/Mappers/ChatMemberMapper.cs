using NeuralDamage.Application.Dtos;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Mappers;

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
