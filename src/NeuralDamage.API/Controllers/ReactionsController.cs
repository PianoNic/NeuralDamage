using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Interfaces;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/chats/{chatId:guid}/messages/{messageId:guid}/reactions")]
public class ReactionsController(ISender sender, IUserResolverService userResolver) : ControllerBase
{
    [HttpPost("{emoji}")]
    public async Task<IActionResult> Toggle(Guid chatId, Guid messageId, string emoji, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new ToggleReactionCommand(chatId, messageId, emoji, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
