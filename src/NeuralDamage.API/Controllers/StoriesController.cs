using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Extensions;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Stories;

namespace NeuralDamage.API.Controllers;

/// <summary>Stories: 24-hour pictures rendered on the server from a spec.</summary>
[ApiController]
[Route("api/stories")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class StoriesController(ISender sender, IUserService userService, AttachmentOptions attachmentOptions) : ControllerBase
{
    private const long MaxRequestBytes = 64L * 1024 * 1024;

    /// <summary>The stories row: everyone's live stories, grouped by author.</summary>
    [HttpGet]
    [ProducesResponseType<List<StoryRingDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StoryRingDto>>> GetAll(CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetStoriesQuery(userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Renders the spec into a story and posts it for 24 hours.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [ProducesResponseType<StoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<StoryDto>> Create(CreateStoryRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CreateStoryCommand(userId, request.Spec, request.SongId, request.MusicStart), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>The PNG the spec would become, exactly as it would be posted; nothing is stored.</summary>
    [HttpPost("preview")]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/png")]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Preview(CreateStoryRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new PreviewStoryCommand(userId, request.Spec), ct);
        return result.IsSuccess ? File(result.Value!, "image/png") : BadRequest(result.Error);
    }

    /// <summary>What the layout check finds in the spec (overlaps, cut-off layers, small text, low contrast): hints for the editor.</summary>
    [HttpPost("layout")]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [ProducesResponseType<List<LayoutIssueDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<List<LayoutIssueDto>>> CheckLayout(CreateStoryRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CheckLayoutCommand(userId, request.Spec), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Uploads a picture for a story being made; the spec names it by the returned <c>ref</c>.</summary>
    [HttpPost("images")]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<StoryImageUploadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<StoryImageUploadDto>> UploadImage(IFormFile file, CancellationToken ct)
    {
        if (file.Length > attachmentOptions.MaxBytes)
            return BadRequest($"Images can be at most {attachmentOptions.MaxBytes / (1024 * 1024.0):0.#} MB.");
        var userId = await userService.GetCurrentUserIdAsync(ct);
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        var result = await sender.Send(new UploadStoryImageCommand(userId, buffer.ToArray()), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{storyId:guid}/image")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/png")]
    [ProducesResponseType<string>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage(Guid storyId, CancellationToken ct)
    {
        var result = await sender.Send(new GetStoryImageQuery(storyId), ct);
        if (result.IsFailure)
            return NotFound(result.Error);
        // A story's picture never changes under its id.
        Response.Headers.CacheControl = "private, max-age=86400, immutable";
        Response.Headers.Vary = "Authorization";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!, "image/png");
    }

    [HttpPost("{storyId:guid}/seen")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> MarkSeen(Guid storyId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new MarkStorySeenCommand(userId, storyId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    /// <summary>Replies in a direct chat with the story's author, made when there is none yet.</summary>
    [HttpPost("{storyId:guid}/reply")]
    [EnableRateLimiting(RateLimitExtensions.Messages)]
    [ProducesResponseType<StoryReplyResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<StoryReplyResultDto>> Reply(Guid storyId, StoryReplyRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new ReplyToStoryCommand(userId, storyId, request.Content), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("{storyId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Delete(Guid storyId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeleteStoryCommand(userId, storyId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
