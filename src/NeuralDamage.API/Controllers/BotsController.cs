using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeuralDamage.Infrastructure.Extensions;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;
using NeuralDamage.Application.Queries;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class BotsController(ISender sender, IUserService userService, AttachmentOptions attachmentOptions) : ControllerBase
{
    /// <summary>A hard ceiling on the request; the configured image limit is checked below it.</summary>
    private const long MaxAvatarRequestBytes = 64L * 1024 * 1024;

    [HttpPost]
    [EnableRateLimiting(RateLimitExtensions.BotCreation)]
    [ProducesResponseType<BotDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BotDto>> Create(CreateBotRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CreateBotCommand(request.Name, request.ModelId, request.SystemPrompt, request.Personality, request.Temperature, request.Aliases, userId, request.IsPublic, request.ChatId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>The public bots; with <paramref name="mine"/>, only the ones the caller made.</summary>
    [HttpGet]
    [ProducesResponseType<List<BotDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<BotDto>>> GetAll([FromQuery] bool mine = false, CancellationToken ct = default)
    {
        Guid? createdById = mine ? await userService.GetCurrentUserIdAsync(ct) : null;
        var result = await sender.Send(new GetBotsQuery(createdById), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{botId:guid}")]
    [ProducesResponseType<BotDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BotDto>> Get(Guid botId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetBotQuery(botId, userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{botId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Update(Guid botId, UpdateBotRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new UpdateBotCommand(botId, userId, request.Name, request.ModelId, request.SystemPrompt, request.Personality, request.Temperature, request.Aliases, request.IsActive), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    /// <summary>Gives the bot its own picture; only its creator can. Returns the new avatar URL.</summary>
    [HttpPut("{botId:guid}/avatar")]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [RequestSizeLimit(MaxAvatarRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAvatarRequestBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<BotAvatarDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BotAvatarDto>> SetAvatar(Guid botId, IFormFile file, CancellationToken ct)
    {
        // Refused before it is read into memory.
        if (file.Length > attachmentOptions.MaxBytes)
            return BadRequest($"Images can be at most {attachmentOptions.MaxBytes / (1024 * 1024.0):0.#} MB.");

        var userId = await userService.GetCurrentUserIdAsync(ct);
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);

        var result = await sender.Send(new SetBotAvatarCommand(botId, userId, buffer.ToArray()), ct);
        return result.IsSuccess ? Ok(new BotAvatarDto(result.Value!)) : BadRequest(result.Error);
    }

    /// <summary>Takes the bot's own picture away, back to its model's icon.</summary>
    [HttpDelete("{botId:guid}/avatar")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RemoveAvatar(Guid botId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new RemoveBotAvatarCommand(botId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet("{botId:guid}/avatar")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/png", "image/jpeg", "image/webp", "image/gif")]
    [ProducesResponseType<string>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvatar(Guid botId, CancellationToken ct)
    {
        var result = await sender.Send(new GetBotAvatarQuery(botId), ct);
        if (result.IsFailure)
            return NotFound(result.Error);

        // Every upload gets a new ?v= in the bot's avatar URL, so this one never changes.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.Vary = "Authorization";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    [HttpDelete("{botId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Delete(Guid botId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeleteBotCommand(botId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet("models")]
    [ProducesResponseType<List<OpenRouterModel>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<OpenRouterModel>>> ListModels(CancellationToken ct)
    {
        var result = await sender.Send(new ListOpenRouterModelsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
