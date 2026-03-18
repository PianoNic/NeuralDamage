using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Interfaces;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/chats/{chatId:guid}/actions")]
public class ChatActionsController(ISender sender, IUserResolverService userResolver) : ControllerBase
{
    [HttpPost("clear")]
    public async Task<IActionResult> Clear(Guid chatId, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new ClearChatCommand(chatId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpPost("kick/{botId:guid}")]
    public async Task<IActionResult> Kick(Guid chatId, Guid botId, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new KickBotCommand(chatId, botId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
