using Mediator;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Dtos;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController(ISender sender) : ControllerBase
{
    /// <summary>
    /// The domain user for the caller. Provisioning already happened during
    /// token validation, so this only reads.
    /// </summary>
    [HttpGet("me", Name = "GetCurrentUser")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken ct)
    {
        var result = await sender.Send(new GetCurrentUserQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
