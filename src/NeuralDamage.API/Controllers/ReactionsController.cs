using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/chats/{chatId:guid}/messages/{messageId:guid}/reactions")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class ReactionsController(ISender sender, IUserService userService) : ControllerBase
{
    [HttpPost("{emoji}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Toggle(Guid chatId, Guid messageId, string emoji, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new ToggleReactionCommand(chatId, messageId, emoji, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
