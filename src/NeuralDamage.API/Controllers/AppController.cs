using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services.Attachments;
using Toamaisutaa.AspNetCore;

namespace NeuralDamage.API.Controllers;

/// <summary>
/// What the SPA reads at startup: where to sign in, the limits it checks
/// before bothering the server, so changing <c>Attachments:*</c> reaches the
/// client too, and the release it shows under the name.
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
            attachments.Limits(),
            Version));
    }

    /// <summary>
    /// The version <c>Directory.Build.props</c> stamps in from <c>application.properties</c>,
    /// without the "+commit" the SDK appends to the informational version.
    /// </summary>
    private static readonly string Version =
        typeof(AppController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppController).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
