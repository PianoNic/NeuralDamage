using System.Text.Json;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Mappers;

public static class MessageMapper
{
    public static MessageDto ToDto(this Message message) => new(
        message.Id,
        message.ChatId,
        message.SenderUserId,
        message.SenderBotId,
        message.Content,
        string.IsNullOrEmpty(message.Mentions) ? null : JsonSerializer.Deserialize<List<string>>(message.Mentions),
        message.ReplyToId,
        message.CreatedAt,
        message.SenderUser?.ToDto(),
        message.SenderBot?.ToSummaryDto(),
        message.Reactions.ToGroups(),
        message.ReplyTo?.ToReplyInfo());

    /// <summary>
    /// Collapses reactions by emoji. Requires the Reactions navigation, with
    /// User and Bot included, to have been loaded.
    /// </summary>
    public static List<ReactionGroupDto> ToGroups(this IEnumerable<Reaction>? reactions)
    {
        if (reactions is null)
            return [];

        return reactions
            .GroupBy(r => r.Emoji)
            .Select(g => new ReactionGroupDto(
                g.Key,
                g.Count(),
                g.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).ToList(),
                g.Where(r => r.BotId is not null).Select(r => r.BotId!.Value).ToList(),
                g.Select(r => r.User?.DisplayName ?? r.Bot?.Name ?? "Unknown").ToList()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Emoji)
            .ToList();
    }

    public static ReplyInfoDto ToReplyInfo(this Message message) => new(
        message.Id,
        message.SenderBot?.Name ?? message.SenderUser?.DisplayName ?? "Unknown",
        message.SenderBotId is not null ? "bot" : "user",
        message.Content);
}
