using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.API.Hubs;

[Authorize]
public class UserHub(IConnectionTracker connectionTracker, IUserService userService) : Hub<IUserClient>
{
    public override async Task OnConnectedAsync()
    {
        var userId = await GetUserIdAsync();
        if (userId == null)
        {
            Context.Abort();
            return;
        }

        connectionTracker.TrackConnection(Context.ConnectionId, userId.Value);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connectionTracker.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// The token's subject is the OIDC external id, not Users.Id.
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
