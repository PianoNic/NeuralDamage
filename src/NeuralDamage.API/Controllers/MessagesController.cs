using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Application.Queries;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/chats/{chatId:guid}/messages")]
public class MessagesController(ISender sender, IUserService userService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send(Guid chatId, SendMessageRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new SendMessageCommand(chatId, userId, request.Content, request.ReplyToId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet]
    public async Task<IActionResult> GetMessages(Guid chatId, [FromQuery] int limit = 50, [FromQuery] DateTime? before = null, CancellationToken ct = default)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetMessagesQuery(chatId, userId, limit, before), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
