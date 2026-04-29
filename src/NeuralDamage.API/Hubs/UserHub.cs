using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.API.Hubs;

[Authorize]
public class UserHub(IConnectionTracker connectionTracker) : Hub<IUserClient>
{
    public override Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (userId == null)
        {
            Context.Abort();
            return Task.CompletedTask;
        }

        connectionTracker.TrackConnection(Context.ConnectionId, userId.Value);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connectionTracker.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private Guid? GetUserId()
    {
        var sub = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
