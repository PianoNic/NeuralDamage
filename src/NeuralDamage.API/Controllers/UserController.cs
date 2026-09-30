using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Dtos;
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
