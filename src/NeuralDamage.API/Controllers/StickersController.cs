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

    private const long MaxImportBytes = 64L * 1024 * 1024;
    private const int MaxImportFiles = 100;

    /// <summary>A page of the library, newest first, searched by name and description.</summary>
    [HttpGet]
    [ProducesResponseType<StickerPageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StickerPageDto>> GetPage([FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = GetStickersQuery.DefaultTake, CancellationToken ct = default)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetStickersQuery(userId, search, skip, take), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>The given stickers still in the library (at most 100), for refreshing ones being described.</summary>
    [HttpGet("by-id")]
    [ProducesResponseType<List<StickerDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StickerDto>>> GetById([FromQuery] List<Guid> ids, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetStickersByIdQuery(userId, ids), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>How many stickers there are, how many are still being described, and whether the caller may import.</summary>
    [HttpGet("status")]
    [ProducesResponseType<StickerLibraryStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StickerLibraryStatusDto>> GetStatus(CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetStickerLibraryStatusQuery(userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>The stickers the caller sent last, most recent first.</summary>
    [HttpGet("recent")]
    [ProducesResponseType<List<StickerDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StickerDto>>> GetRecent(CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetRecentStickersQuery(userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>
    /// Admins only: adds up to 100 stickers at once, skipping images the library
    /// already has. They are described in the background, a few at a time.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(MaxImportBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<StickerImportResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StickerImportResultDto>> Import(List<IFormFile> files, CancellationToken ct)
    {
        if (files.Count == 0)
            return BadRequest("No files.");
        if (files.Count > MaxImportFiles)
            return BadRequest($"At most {MaxImportFiles} files at once.");

        var uploads = new List<StickerFileUpload>(files.Count);
        var tooBig = new List<StickerImportFailureDto>();
        foreach (var file in files)
        {
            if (file.Length > StickerRules.MaxBytes)
            {
                tooBig.Add(new StickerImportFailureDto(file.FileName, $"Stickers can be at most {StickerRules.MaxBytes / (1024 * 1024)} MB."));
                continue;
            }
            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, ct);
            uploads.Add(new StickerFileUpload(file.FileName, buffer.ToArray()));
        }

        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new ImportStickersCommand(userId, uploads), ct);
        if (result.IsFailure)
            return result.Error == ImportStickersHandler.AdminsOnly ? StatusCode(StatusCodes.Status403Forbidden, result.Error) : BadRequest(result.Error);
        return Ok(result.Value! with { Failed = [.. tooBig, .. result.Value.Failed] });
    }

    /// <summary>Adds a sticker; its description is written in the background. One the library already has is refused.</summary>
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
