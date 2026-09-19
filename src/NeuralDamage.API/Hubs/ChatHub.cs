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

        logger.LogInformation("Connection {Conn} joined {Count} chat group(s) on connect", Context.ConnectionId, chatIds.Count);

        await base.OnConnectedAsync();
    }

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
