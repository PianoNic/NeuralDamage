using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Dtos.Requests;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Queries;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/chats/{chatId:guid}/messages")]
public class MessagesController(ISender sender, IUserResolverService userResolver) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send(Guid chatId, SendMessageRequest request, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new SendMessageCommand(chatId, userId, request.Content, request.ReplyToId), ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet]
    public async Task<IActionResult> GetMessages(Guid chatId, [FromQuery] int limit = 50, [FromQuery] DateTime? before = null, CancellationToken ct = default)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetMessagesQuery(chatId, userId, limit, before), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
