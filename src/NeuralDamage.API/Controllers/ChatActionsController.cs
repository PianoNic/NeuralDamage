using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/chats/{chatId:guid}/actions")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class ChatActionsController(ISender sender, IUserService userService) : ControllerBase
{
    [HttpPost("clear")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Clear(Guid chatId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new ClearChatCommand(chatId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpPost("kick/{botId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Kick(Guid chatId, Guid botId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new KickBotCommand(chatId, botId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
