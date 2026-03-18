using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Interfaces;

namespace NeuralDamage.API.Hubs;

[Authorize]
public class ChatHub(IConnectionTracker connectionTracker, INeuralDamageDbContext db) : Hub<IChatClient>
{
    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (userId == null)
        {
            Context.Abort();
            return;
        }

        connectionTracker.TrackConnection(Context.ConnectionId, userId.Value);

        var chatIds = await db.ChatMembers
            .Where(cm => cm.UserId == userId.Value)
            .Select(cm => cm.ChatId)
            .ToListAsync();

        foreach (var chatId in chatIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connectionTracker.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChat(Guid chatId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
    }

    public async Task LeaveChat(Guid chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());
    }

    private Guid? GetUserId()
    {
        var sub = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
