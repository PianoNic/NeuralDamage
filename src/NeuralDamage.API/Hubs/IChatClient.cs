using NeuralDamage.Infrastructure.Dtos;

namespace NeuralDamage.API.Hubs;

public interface IChatClient
{
    Task ChatUpdated(ChatDto chat);
    Task ChatDeleted(Guid chatId);
    Task ChatCleared(Guid chatId);
    Task MemberAdded(ChatMemberDto member);
    Task MemberRemoved(Guid chatId, Guid memberId);
    Task MessageNew(MessageDto message);
    Task ReactionUpdated(Guid messageId, List<ReactionGroupDto> reactions);
    Task BotTyping(Guid chatId, Guid botId, string botName);
    Task UserTyping(Guid chatId, Guid userId, string displayName);
    Task BotResponseCancelled(Guid chatId);
    Task SystemMessage(SystemMessageDto message);
}
