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
        message.ReplyTo?.ToReplyInfo(),
        message.Attachments.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Select(a => a.ToDto()).ToList(),
        message.Sticker?.ToMessageDto());

    public static MessageStickerDto ToMessageDto(this Sticker sticker) => new(
        sticker.Id,
        sticker.Name,
        StickerUrl(sticker.Id),
        sticker.Width,
        sticker.Height,
        sticker.Animated,
        sticker.Description);

    /// <summary>Requires UploaderUser to be loaded.</summary>
    public static StickerDto ToDto(this Sticker sticker, bool canEdit) => new(
        sticker.Id,
        sticker.Name,
        StickerUrl(sticker.Id),
        sticker.ContentType,
        sticker.Width,
        sticker.Height,
        sticker.Animated,
        sticker.Description,
        // A run cut short by a restart never clears the flag; past a couple of minutes it is not describing anything.
        sticker.Describing && sticker.Description is null && DateTime.UtcNow - sticker.UpdatedAt < TimeSpan.FromMinutes(2),
        sticker.UseCount,
        sticker.UploaderUserId,
        sticker.UploaderUser?.DisplayName ?? "Unknown",
        sticker.CreatedAt,
        canEdit);

    /// <summary>A sticker's file never changes under its id, so the URL can be cached for good.</summary>
    public static string StickerUrl(Guid stickerId) => $"/api/stickers/{stickerId}/image";

    public static AttachmentDto ToDto(this Attachment attachment) => new(
        attachment.Id,
        $"/api/chats/{attachment.ChatId}/attachments/{attachment.Id}",
        attachment.ContentType,
        attachment.SizeBytes,
        attachment.Width,
        attachment.Height,
        attachment.Description);

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
        message.StickerId is not null && message.Content.Length == 0 ? "Sticker" : message.Content);
}
