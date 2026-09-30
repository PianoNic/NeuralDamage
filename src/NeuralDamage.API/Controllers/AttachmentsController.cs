using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Extensions;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.API.Controllers;

/// <summary>Images for messages: uploaded before the message is sent, served to the chat's members only.</summary>
[ApiController]
[Route("api/chats/{chatId:guid}/attachments")]
public class AttachmentsController(ISender sender, IUserService userService, AttachmentOptions options) : ControllerBase
{
    /// <summary>A hard ceiling on the request; the configured image limit is checked below it.</summary>
    private const long MaxRequestBytes = 64L * 1024 * 1024;

    [HttpPost]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<AttachmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AttachmentDto>> Upload(Guid chatId, IFormFile file, CancellationToken ct)
    {
        // Refused before it is read into memory.
        if (file.Length > options.MaxBytes)
            return BadRequest($"Images can be at most {options.MaxBytes / (1024 * 1024.0):0.#} MB.");

        var userId = await userService.GetCurrentUserIdAsync(ct);
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);

        var result = await sender.Send(new UploadAttachmentCommand(chatId, userId, buffer.ToArray()), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{attachmentId:guid}")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, "image/png", "image/jpeg", "image/webp", "image/gif")]
    [ProducesResponseType<string>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid chatId, Guid attachmentId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetAttachmentQuery(chatId, attachmentId, userId), ct);
        if (result.IsFailure)
            return NotFound(result.Error);

        // An image never changes under its id, but it is private to the chat: the
        // browser cache is keyed on the token too, or someone else signing in on
        // the same browser would be served it from the cache.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.Vary = "Authorization";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }
}
