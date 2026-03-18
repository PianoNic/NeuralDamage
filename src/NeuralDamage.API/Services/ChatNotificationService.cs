using Microsoft.AspNetCore.SignalR;
using NeuralDamage.API.Hubs;
using NeuralDamage.Application.Dtos;
using NeuralDamage.Application.Interfaces;

namespace NeuralDamage.API.Services;

public class ChatNotificationService(
    IHubContext<ChatHub, IChatClient> chatHub,
    IHubContext<UserHub, IUserClient> userHub,
    IConnectionTracker connectionTracker) : IChatNotificationService
{
    // User-level notifications via UserHub
    public async Task NotifyUserChatCreated(Guid userId, ChatDto chat)
    {
        var connections = connectionTracker.GetConnections(userId);
        foreach (var connId in connections)
            await userHub.Clients.Client(connId).ChatCreated(chat);
    }

    public async Task NotifyUserChatJoined(Guid userId, ChatDetailDto chat)
    {
        var connections = connectionTracker.GetConnections(userId);
        foreach (var connId in connections)
            await userHub.Clients.Client(connId).ChatJoined(chat);
    }

    public async Task NotifyUserChatLeft(Guid userId, Guid chatId)
    {
        var connections = connectionTracker.GetConnections(userId);
        foreach (var connId in connections)
            await userHub.Clients.Client(connId).ChatLeft(chatId);
    }

    // Chat group broadcasts via ChatHub
    public Task NotifyChatUpdated(Guid chatId, ChatDto chat) => chatHub.Clients.Group(chatId.ToString()).ChatUpdated(chat);
    public Task NotifyChatDeleted(Guid chatId) => chatHub.Clients.Group(chatId.ToString()).ChatDeleted(chatId);
    public Task NotifyChatCleared(Guid chatId) => chatHub.Clients.Group(chatId.ToString()).ChatCleared(chatId);
    public Task NotifyMessageNew(Guid chatId, MessageDto message) => chatHub.Clients.Group(chatId.ToString()).MessageNew(message);
    public Task NotifyMemberAdded(Guid chatId, ChatMemberDto member) => chatHub.Clients.Group(chatId.ToString()).MemberAdded(member);
    public Task NotifyMemberRemoved(Guid chatId, Guid memberId) => chatHub.Clients.Group(chatId.ToString()).MemberRemoved(chatId, memberId);
    public Task NotifyReactionUpdated(Guid chatId, Guid messageId, List<ReactionDto> reactions) => chatHub.Clients.Group(chatId.ToString()).ReactionUpdated(messageId, reactions);
    public Task NotifyBotTyping(Guid chatId, Guid botId, string botName) => chatHub.Clients.Group(chatId.ToString()).BotTyping(chatId, botId, botName);
    public Task NotifyBotResponseCancelled(Guid chatId) => chatHub.Clients.Group(chatId.ToString()).BotResponseCancelled(chatId);
}
