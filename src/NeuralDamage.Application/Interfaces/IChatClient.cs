using NeuralDamage.Application.Dtos;

namespace NeuralDamage.Application.Interfaces;

public interface IChatClient
{
    Task ChatUpdated(ChatDto chat);
    Task ChatDeleted(Guid chatId);
    Task ChatCleared(Guid chatId);
    Task MemberAdded(ChatMemberDto member);
    Task MemberRemoved(Guid chatId, Guid memberId);
    Task MessageNew(MessageDto message);
    Task ReactionUpdated(Guid messageId, List<ReactionDto> reactions);
    Task BotTyping(Guid chatId, Guid botId, string botName);
    Task BotResponseCancelled(Guid chatId);
}
