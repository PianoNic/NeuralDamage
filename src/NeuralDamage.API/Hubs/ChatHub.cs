using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.API.Hubs;

[Authorize]
public class ChatHub(NeuralDamageDbContext db, IUserService userService) : Hub<IChatClient>
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

        await base.OnConnectedAsync();
    }

    public async Task JoinChat(Guid chatId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
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
