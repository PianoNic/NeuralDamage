namespace NeuralDamage.Infrastructure.Dtos;

public record MessageDto(
    Guid Id,
    Guid ChatId,
    Guid? SenderUserId,
    Guid? SenderBotId,
    string Content,
    List<string>? Mentions,
    Guid? ReplyToId,
    DateTime CreatedAt,
    UserDto? SenderUser,
    BotSummaryDto? SenderBot,
    List<ReactionGroupDto> Reactions,
    ReplyInfoDto? ReplyTo,
    List<AttachmentDto> Attachments,
    MessageStickerDto? Sticker = null,
    StoryReplyDto? Story = null);

/// <summary>The story a message replies to: whose it was and what it said.</summary>
public record StoryReplyDto(Guid StoryId, string AuthorName, Guid? AuthorUserId, Guid? AuthorBotId, string Text);
