using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Extensions;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.API.Controllers;

/// <summary>
/// Music for posts and stories: songs found on Deezer, stored once picked, and their audio,
/// which needs no sign-in so an audio element can stream and seek it.
/// </summary>
[ApiController]
[Route("api/music")]
public class MusicController(ISender sender, IUserService userService) : ControllerBase
{
    /// <summary>Songs for <paramref name="q"/>; with no search, ones matching <paramref name="forText"/> and the chart, or just the chart.</summary>
    [HttpGet("search")]
    [EnableRateLimiting(RateLimitExtensions.Messages)]
    [ProducesResponseType<List<SongResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<List<SongResultDto>>> Search([FromQuery] string? q, [FromQuery] string? forText, CancellationToken ct)
    {
        await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new FindSongsQuery(q, forText), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Stores a picked song with its preview, and starts fetching the whole song.</summary>
    [HttpPost("songs")]
    [EnableRateLimiting(RateLimitExtensions.Messages)]
    [ProducesResponseType<SongDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SongDto>> Add(AddSongRequest request, CancellationToken ct)
    {
        await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new AddSongCommand(request.DeezerId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>A stored song: whether its whole song is there yet.</summary>
    [HttpGet("songs/{songId:guid}")]
    [ProducesResponseType<SongDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SongDto>> Get(Guid songId, CancellationToken ct)
    {
        await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetSongQuery(songId), ct);
        return result.IsSuccess ? Ok(result.Value) : NotFound(result.Error);
    }

    /// <summary>A song's 30-second preview, streamed.</summary>
    [HttpGet("songs/{songId:guid}/preview")]
    [AllowAnonymous]
    public Task<IActionResult> Preview(Guid songId, CancellationToken ct) => AudioAsync(songId, false, ct);

    /// <summary>A song's whole audio, streamed; players seek to the clip with range requests.</summary>
    [HttpGet("songs/{songId:guid}/full")]
    [AllowAnonymous]
    public Task<IActionResult> Full(Guid songId, CancellationToken ct) => AudioAsync(songId, true, ct);

    private async Task<IActionResult> AudioAsync(Guid songId, bool full, CancellationToken ct)
    {
        var result = await sender.Send(new GetSongAudioQuery(songId, full), ct);
        if (result.IsFailure)
            return NotFound(result.Error);
        // A song's file never changes once stored.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(result.Value!.Path, result.Value.ContentType, enableRangeProcessing: true);
    }
}
