using Microsoft.AspNetCore.SignalR;
using NeuralDamage.API.Hubs;
using NeuralDamage.Application.Dtos;
using NeuralDamage.Application.Interfaces;

namespace NeuralDamage.API.Services;

public class ChatNotificationService(IHubContext<ChatHub, IChatClient> hub) : IChatNotificationService
{
    public Task NotifyChatUpdated(Guid chatId, ChatDto chat) => hub.Clients.Group(chatId.ToString()).ChatUpdated(chat);
    public Task NotifyChatDeleted(Guid chatId) => hub.Clients.Group(chatId.ToString()).ChatDeleted(chatId);
    public Task NotifyMessageNew(Guid chatId, MessageDto message) => hub.Clients.Group(chatId.ToString()).MessageNew(message);
    public Task NotifyMemberAdded(Guid chatId, ChatMemberDto member) => hub.Clients.Group(chatId.ToString()).MemberAdded(member);
    public Task NotifyMemberRemoved(Guid chatId, Guid memberId) => hub.Clients.Group(chatId.ToString()).MemberRemoved(chatId, memberId);
    public Task NotifyReactionUpdated(Guid chatId, Guid messageId, List<ReactionDto> reactions) => hub.Clients.Group(chatId.ToString()).ReactionUpdated(messageId, reactions);
    public Task NotifyBotTyping(Guid chatId, Guid botId, string botName) => hub.Clients.Group(chatId.ToString()).BotTyping(chatId, botId, botName);
    public Task NotifyBotResponseCancelled(Guid chatId) => hub.Clients.Group(chatId.ToString()).BotResponseCancelled(chatId);
}
