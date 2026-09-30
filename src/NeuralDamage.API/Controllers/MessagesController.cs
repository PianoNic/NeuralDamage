using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeuralDamage.Infrastructure.Extensions;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;
using NeuralDamage.Application.Queries;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/chats/{chatId:guid}/messages")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class MessagesController(ISender sender, IUserService userService) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitExtensions.Messages)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Send(Guid chatId, SendMessageRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new SendMessageCommand(chatId, userId, request.Content, request.ReplyToId, request.AttachmentIds), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet]
    [ProducesResponseType<List<MessageDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<MessageDto>>> GetMessages(Guid chatId, [FromQuery] int limit = 50, [FromQuery] DateTime? before = null, CancellationToken ct = default)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetMessagesQuery(chatId, userId, limit, before), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
