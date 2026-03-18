using NeuralDamage.Application.Dtos;

namespace NeuralDamage.Application.Interfaces;

public interface IChatNotificationService
{
    Task NotifyChatUpdated(Guid chatId, ChatDto chat);
    Task NotifyChatDeleted(Guid chatId);
    Task NotifyMessageNew(Guid chatId, MessageDto message);
    Task NotifyMemberAdded(Guid chatId, ChatMemberDto member);
    Task NotifyMemberRemoved(Guid chatId, Guid memberId);
    Task NotifyReactionUpdated(Guid chatId, Guid messageId, List<ReactionDto> reactions);
    Task NotifyBotTyping(Guid chatId, Guid botId, string botName);
    Task NotifyBotResponseCancelled(Guid chatId);
}
