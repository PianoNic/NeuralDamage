using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Application.Dtos;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("config", Name = "GetAppConfig")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AppConfigDto), StatusCodes.Status200OK)]
    public IActionResult GetConfig()
    {
        var config = new AppConfigDto(
            configuration["Oidc:Authority"] ?? string.Empty,
            configuration["Oidc:ClientId"] ?? string.Empty,
            configuration["Oidc:RedirectUri"] ?? "http://localhost:4200/callback",
            configuration["Oidc:PostLogoutRedirectUri"] ?? "http://localhost:4200/",
            configuration["Oidc:Scope"] ?? "openid profile email");

        return Ok(config);
    }
}
