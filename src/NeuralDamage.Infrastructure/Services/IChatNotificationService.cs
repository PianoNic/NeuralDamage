using NeuralDamage.Infrastructure.Dtos;

namespace NeuralDamage.Infrastructure.Services;

public interface IChatNotificationService
{
    // User-level (via UserHub)
    Task NotifyUserChatCreated(Guid userId, ChatDto chat);
    Task NotifyUserChatJoined(Guid userId, ChatDetailDto chat);
    Task NotifyUserChatLeft(Guid userId, Guid chatId);

    // Chat group (via ChatHub)
    Task NotifyChatUpdated(Guid chatId, ChatDto chat);
    Task NotifyChatDeleted(Guid chatId);
    Task NotifyChatCleared(Guid chatId);
    Task NotifyMessageNew(Guid chatId, MessageDto message);
    Task NotifyMemberAdded(Guid chatId, ChatMemberDto member);
    Task NotifyMemberRemoved(Guid chatId, Guid memberId);
    Task NotifyReactionUpdated(Guid chatId, Guid messageId, List<ReactionDto> reactions);
    Task NotifyBotTyping(Guid chatId, Guid botId, string botName);
    Task NotifyBotResponseCancelled(Guid chatId);
}
