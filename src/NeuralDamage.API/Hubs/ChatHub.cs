using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.API.Hubs;

[Authorize]
public class ChatHub(NeuralDamageDbContext db, IUserService userService, ILogger<ChatHub> logger) : Hub<IChatClient>
{
    public override async Task OnConnectedAsync()
    {
        var userId = await GetUserIdAsync();
        if (userId == null) { Context.Abort(); return; }

        var chatIds = await db.ChatMembers
            .Where(cm => cm.UserId == userId.Value)
            .Select(cm => cm.ChatId)
            .ToListAsync();

        foreach (var chatId in chatIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId.Value));

        logger.LogInformation("Connection {Conn} joined {Count} chat group(s) on connect", Context.ConnectionId, chatIds.Count);

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Every connection of one user, for what only they should see. SignalR's
    /// own Clients.User keys on the token's subject, not on Users.Id, so the
    /// hub keeps a group per user instead.
    /// </summary>
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public async Task JoinChat(Guid chatId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
        logger.LogInformation("Connection {Conn} joined chat group {ChatId}", Context.ConnectionId, chatId);
    }

    public async Task LeaveChat(Guid chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());
    }

    /// <summary>
    /// Tells the chat's other members this user is typing. Clients call it
    /// repeatedly (throttled) while typing and expire the indicator themselves,
    /// so there is no matching "stop".
    /// </summary>
    public async Task StartTyping(Guid chatId)
    {
        var userId = await GetUserIdAsync();
        if (userId == null) return;

        var displayName = await db.ChatMembers
            .Where(cm => cm.ChatId == chatId && cm.UserId == userId.Value)
            .Select(cm => cm.User!.DisplayName != "" ? cm.User.DisplayName : cm.User.Email)
            .FirstOrDefaultAsync(Context.ConnectionAborted);
        if (displayName == null) return;

        await Clients.OthersInGroup(chatId.ToString()).UserTyping(chatId, userId.Value, displayName);
    }

    /// <summary>
    /// The token's subject is the OIDC external id, not Users.Id, so it has to be
    /// translated before it can be matched against ChatMembers.
    /// </summary>
    private async Task<Guid?> GetUserIdAsync()
    {
        var sub = Context.User?.FindFirstValue("sub")
            ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        return string.IsNullOrEmpty(sub)
            ? null
            : await userService.GetUserIdByExternalIdAsync(sub, Context.ConnectionAborted);
    }
}
