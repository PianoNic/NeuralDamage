using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class UserController(ISender sender, IUserService userService) : ControllerBase
{
    /// <summary>
    /// The domain user for the caller. Provisioning already happened during
    /// token validation, so this only reads.
    /// </summary>
    [HttpGet("me", Name = "GetCurrentUser")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> GetCurrentUser(CancellationToken ct)
    {
        var result = await sender.Send(new GetCurrentUserQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>The names you also go by, so "@Niclas" mentions you when you are PianoNic.</summary>
    [HttpPut("me/nicknames", Name = "UpdateNicknames")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> UpdateNicknames(UpdateNicknamesRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new UpdateNicknamesCommand(userId, request.Nicknames), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>What every bot remembers about you.</summary>
    [HttpGet("me/memories", Name = "GetMemoriesAboutMe")]
    [ProducesResponseType<List<BotMemoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<BotMemoryDto>>> GetMemoriesAboutMe(CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetMemoriesAboutMeQuery(userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Makes a bot forget one thing it remembers about you.</summary>
    [HttpDelete("me/memories/{memoryId:guid}", Name = "ForgetMemoryAboutMe")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgetMemoryAboutMe(Guid memoryId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeleteBotMemoryCommand(memoryId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    /// <summary>Whether bots may remember things about you; turning it off forgets what they had.</summary>
    [HttpPut("me/bot-memory", Name = "AllowBotMemory")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> AllowBotMemory(AllowBotMemoryRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new AllowBotMemoryCommand(userId, request.Allowed), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>
    /// Other users, for inviting into a chat. Pass <paramref name="chatId"/> to
    /// leave out that chat's current members.
    /// </summary>
    [HttpGet("/api/users", Name = "GetUsers")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<List<UserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<UserDto>>> GetUsers([FromQuery] string? search = null, [FromQuery] Guid? chatId = null, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetUsersQuery(userId, search, chatId, limit), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
