using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeuralDamage.Infrastructure.Extensions;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.API.Hubs;
using NeuralDamage.Infrastructure;
using NeuralDamage.Application.Queries;

namespace NeuralDamage.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class ChatsController(ISender sender, IUserService userService, AttachmentOptions attachmentOptions) : ControllerBase
{
    private const long MaxAvatarRequestBytes = 64L * 1024 * 1024;

    [HttpPost]
    [ProducesResponseType<ChatDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatDto>> Create(CreateChatRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CreateChatCommand(request.Name, userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>The direct chat with a bot or a person, made the first time.</summary>
    [HttpPost("direct")]
    [ProducesResponseType<ChatDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatDto>> OpenDirect(OpenDirectChatRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new OpenDirectChatCommand(userId, request.BotId, request.UserId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet]
    [ProducesResponseType<List<ChatDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ChatDto>>> GetAll(CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetUserChatsQuery(userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{chatId:guid}")]
    [ProducesResponseType<ChatDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatDetailDto>> Get(Guid chatId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetChatQuery(chatId, userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{chatId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Update(Guid chatId, UpdateChatRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new UpdateChatCommand(chatId, request.Name, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    /// <summary>Gives a group chat its own picture; only its owner can. Returns the new avatar URL.</summary>
    [HttpPut("{chatId:guid}/avatar")]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [RequestSizeLimit(MaxAvatarRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAvatarRequestBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ChatAvatarDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChatAvatarDto>> SetAvatar(Guid chatId, IFormFile file, CancellationToken ct)
    {
        // Refused before it is read into memory.
        if (file.Length > attachmentOptions.MaxBytes)
            return BadRequest($"Images can be at most {attachmentOptions.MaxBytes / (1024 * 1024.0):0.#} MB.");

        var userId = await userService.GetCurrentUserIdAsync(ct);
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);

        var result = await sender.Send(new SetChatAvatarCommand(chatId, userId, buffer.ToArray()), ct);
        return result.IsSuccess ? Ok(new ChatAvatarDto(result.Value!)) : BadRequest(result.Error);
    }

    /// <summary>Takes the group chat's picture away.</summary>
    [HttpDelete("{chatId:guid}/avatar")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RemoveAvatar(Guid chatId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new RemoveChatAvatarCommand(chatId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet("{chatId:guid}/avatar")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/png", "image/jpeg", "image/webp", "image/gif")]
    [ProducesResponseType<string>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvatar(Guid chatId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetChatAvatarQuery(chatId, userId), ct);
        if (result.IsFailure)
            return NotFound(result.Error);

        // Every upload gets a new ?v= in the chat's avatar URL, so this one never changes.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.Vary = "Authorization";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    [HttpDelete("{chatId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Delete(Guid chatId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeleteChatCommand(chatId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
