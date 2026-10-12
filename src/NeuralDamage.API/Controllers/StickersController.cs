using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Extensions;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Stickers;

namespace NeuralDamage.API.Controllers;

/// <summary>The app's one shared sticker library: anyone can upload, and people and bots can send every sticker.</summary>
[ApiController]
[Route("api/stickers")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class StickersController(ISender sender, IUserService userService) : ControllerBase
{
    /// <summary>A hard ceiling on the request; the sticker limit is checked below it.</summary>
    private const long MaxRequestBytes = 16L * 1024 * 1024;

    [HttpGet]
    [ProducesResponseType<List<StickerDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StickerDto>>> GetAll(CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetStickersQuery(userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>The ids of the stickers the caller sent last, most recent first.</summary>
    [HttpGet("recent")]
    [ProducesResponseType<List<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Guid>>> GetRecent(CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetRecentStickersQuery(userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Adds a sticker; its description is written in the background.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<StickerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<StickerDto>> Upload(IFormFile file, [FromForm] string? name, CancellationToken ct)
    {
        // Refused before it is read into memory.
        if (file.Length > StickerRules.MaxBytes)
            return BadRequest($"Stickers can be at most {StickerRules.MaxBytes / (1024 * 1024)} MB.");

        var userId = await userService.GetCurrentUserIdAsync(ct);
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);

        var result = await sender.Send(new UploadStickerCommand(userId, name ?? Path.GetFileNameWithoutExtension(file.FileName), buffer.ToArray()), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{stickerId:guid}")]
    [ProducesResponseType<StickerDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StickerDto>> Update(Guid stickerId, UpdateStickerRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new UpdateStickerCommand(stickerId, userId, request.Name, request.Description), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("{stickerId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Delete(Guid stickerId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeleteStickerCommand(stickerId, userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpGet("{stickerId:guid}/image")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/png", "image/webp", "image/gif")]
    [ProducesResponseType<string>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage(Guid stickerId, CancellationToken ct)
    {
        var result = await sender.Send(new GetStickerImageQuery(stickerId), ct);
        if (result.IsFailure)
            return NotFound(result.Error);

        // A sticker's file never changes under its id.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.Vary = "Authorization";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }
}
