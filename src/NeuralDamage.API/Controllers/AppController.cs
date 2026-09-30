using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services.Attachments;
using Toamaisutaa.AspNetCore;

namespace NeuralDamage.API.Controllers;

/// <summary>
/// What the SPA reads at startup: where to sign in, and the limits it checks
/// before bothering the server, so changing <c>Attachments:*</c> reaches the
/// client too.
/// </summary>
[ApiController]
[Route("api/app")]
[AllowAnonymous]
[Tags("Application Configuration")]
public class AppController(IToamaisutaaClientConfigurationProvider oidc, AttachmentOptions attachments) : ControllerBase
{
    [HttpGet(Name = "appConfiguration")]
    [ProducesResponseType<AppConfigurationDto>(StatusCodes.Status200OK)]
    public ActionResult<AppConfigurationDto> Get()
    {
        var config = oidc.GetConfiguration(HttpContext);
        return Ok(new AppConfigurationDto(
            config.Authority,
            config.ClientId,
            config.RedirectUri,
            config.PostLogoutRedirectUri,
            config.Scope,
            attachments.Limits()));
    }
}
