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
[Route("api/[controller]")]
public class ChatsController(ISender sender, IUserService userService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateChatRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CreateChatCommand(request.Name, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetUserChatsQuery(userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{chatId:guid}")]
    public async Task<IActionResult> Get(Guid chatId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetChatQuery(chatId, userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{chatId:guid}")]
    public async Task<IActionResult> Update(Guid chatId, UpdateChatRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new UpdateChatCommand(chatId, request.Name, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpDelete("{chatId:guid}")]
    public async Task<IActionResult> Delete(Guid chatId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeleteChatCommand(chatId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
