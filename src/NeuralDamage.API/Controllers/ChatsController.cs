using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Dtos.Requests;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Queries;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatsController(ISender sender, IUserResolverService userResolver) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateChatRequest request, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CreateChatCommand(request.Name, userId), ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetUserChatsQuery(userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{chatId:guid}")]
    public async Task<IActionResult> Get(Guid chatId, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetChatQuery(chatId, userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{chatId:guid}")]
    public async Task<IActionResult> Update(Guid chatId, UpdateChatRequest request, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new UpdateChatCommand(chatId, request.Name, userId), ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpDelete("{chatId:guid}")]
    public async Task<IActionResult> Delete(Guid chatId, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeleteChatCommand(chatId, userId), ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }
}
