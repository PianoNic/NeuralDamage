using System.Text.Json;
using NeuralDamage.Application.Dtos;
using NeuralDamage.Domain;

namespace NeuralDamage.Application.Mappers;

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
        message.SenderBot?.ToSummaryDto());
}
