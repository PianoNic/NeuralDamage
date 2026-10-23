using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Extensions;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.API.Controllers;

/// <summary>
/// Generated pictures: asked for with a prompt, drawn one at a time by the
/// self-hosted generator, then attached to a post like an upload.
/// </summary>
[ApiController]
[Route("api/images")]
public class ImagesController(ISender sender, IUserService userService) : ControllerBase
{
    /// <summary>Starts drawing a picture; poll it until its status is Ready or Failed.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [ProducesResponseType<GeneratedImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<GeneratedImageDto>> Generate(GenerateImageRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GenerateImageCommand(userId, request.Prompt, request.FaceBotId, request.ForStory, request.Format), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Renders layers into a 4:5 or square picture for a post, like a story; it is ready at once.</summary>
    [HttpPost("compose")]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [ProducesResponseType<GeneratedImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<GeneratedImageDto>> Compose(ComposeImageRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new ComposeImageCommand(userId, request.Spec), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Searches the web for pictures (when <c>ImageSearch:BaseUrl</c> is set); thumbnails come through this API.</summary>
    [HttpGet("search")]
    [EnableRateLimiting(RateLimitExtensions.Messages)]
    [ProducesResponseType<List<WebImageResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<List<WebImageResultDto>>> Search([FromQuery] string q, CancellationToken ct)
    {
        await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new SearchWebImagesQuery(q ?? ""), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>A small picture of one search result, fetched by the server so the reader's browser never calls the web.</summary>
    [HttpGet("search/thumbnail")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, "image/png", "image/jpeg", "image/webp", "image/gif")]
    [ProducesResponseType<string>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SearchThumbnail([FromQuery] string q, [FromQuery] int index, CancellationToken ct)
    {
        await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetWebImageThumbnailQuery(q ?? "", index), ct);
        if (result.IsFailure)
            return NotFound(result.Error);
        Response.Headers.CacheControl = "private, max-age=900";
        Response.Headers.Vary = "Authorization";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    /// <summary>Fetches one search result and keeps it for the caller's post or story; use it as <c>generated:&lt;id&gt;</c> or as the post's picture.</summary>
    [HttpPost("search/pick")]
    [EnableRateLimiting(RateLimitExtensions.Uploads)]
    [ProducesResponseType<GeneratedImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<GeneratedImageDto>> PickSearchResult(PickWebImageRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new PickWebImageCommand(userId, request.Query, request.Index), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>The caller's own picture: whether it is done yet.</summary>
    [HttpGet("{imageId:guid}")]
    [ProducesResponseType<GeneratedImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GeneratedImageDto>> Get(Guid imageId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetGeneratedImageQuery(userId, imageId), ct);
        return result.IsSuccess ? Ok(result.Value) : NotFound(result.Error);
    }

    /// <summary>The picture itself: for its maker, and for everyone once it is on a post.</summary>
    [HttpGet("{imageId:guid}/file")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, "image/png", "image/jpeg", "image/webp")]
    [ProducesResponseType<string>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(Guid imageId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetGeneratedImageFileQuery(userId, imageId), ct);
        if (result.IsFailure)
            return NotFound(result.Error);

        // Like an attachment: it never changes under its id, but it is only for those signed in.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.Vary = "Authorization";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    /// <summary>Throws away a picture the caller made and did not post.</summary>
    [HttpDelete("{imageId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Discard(Guid imageId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DiscardImageCommand(userId, imageId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
