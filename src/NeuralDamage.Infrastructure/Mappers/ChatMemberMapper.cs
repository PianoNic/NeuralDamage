using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Infrastructure.Mappers;

public static class ChatMemberMapper
{
    public static ChatMemberDto ToDto(this ChatMember member, bool isMuted = false, ModelStatus? modelStatus = null) => new(
        member.Id,
        member.ChatId,
        member.UserId,
        member.BotId,
        member.Role.ToString(),
        member.JoinedAt,
        member.User?.ToDto(),
        member.Bot?.ToSummaryDto(),
        isMuted,
        member.Bot is null ? null : (modelStatus ?? ModelStatus.Ok).Status,
        modelStatus?.Reason);

    /// <summary>Maps the member with its current mute state and, for a bot, whether its model still works.</summary>
    public static ChatMemberDto ToDto(this ChatMember member, IChatBotState botState, Func<string, ModelStatus> modelLookup) =>
        member.ToDto(
            member.BotId is { } botId && botState.IsMuted(member.ChatId, botId),
            member.Bot is { } bot ? modelLookup(bot.ModelId) : null);
}
