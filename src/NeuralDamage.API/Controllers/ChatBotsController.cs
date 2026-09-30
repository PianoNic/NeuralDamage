using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/chats/{chatId:guid}/bots/{botId:guid}")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class ChatBotsController(ISender sender, IUserService userService) : ControllerBase
{
    [HttpPost("mute")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public Task<IActionResult> Mute(Guid chatId, Guid botId, CancellationToken ct) => SetMuted(chatId, botId, true, ct);

    [HttpPost("unmute")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public Task<IActionResult> Unmute(Guid chatId, Guid botId, CancellationToken ct) => SetMuted(chatId, botId, false, ct);

    private async Task<IActionResult> SetMuted(Guid chatId, Guid botId, bool muted, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new SetBotMutedCommand(chatId, botId, userId, muted), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
