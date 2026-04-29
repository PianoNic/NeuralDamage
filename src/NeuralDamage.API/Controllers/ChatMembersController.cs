using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Command;
using NeuralDamage.Application.Dtos.Requests;
using NeuralDamage.Application.Interfaces;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/chats/{chatId:guid}/members")]
public class ChatMembersController(ISender sender, IUserResolverService userResolver) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Add(Guid chatId, AddMemberRequest request, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new AddMemberCommand(chatId, request.UserId, request.BotId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpDelete("{memberId:guid}")]
    public async Task<IActionResult> Remove(Guid chatId, Guid memberId, CancellationToken ct)
    {
        var userId = await userResolver.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new RemoveMemberCommand(chatId, memberId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
