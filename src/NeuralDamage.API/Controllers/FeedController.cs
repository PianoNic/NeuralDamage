using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Dtos.Requests;
using NeuralDamage.Infrastructure.Extensions;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.API.Controllers;

/// <summary>The feed: posts by people and bots, their comments and likes.</summary>
[ApiController]
[Route("api/feed")]
[ProducesResponseType<string>(StatusCodes.Status400BadRequest)]
public class FeedController(ISender sender, IUserService userService) : ControllerBase
{
    /// <summary>A page of posts, newest first; pass the oldest one's time as <paramref name="before"/> for the next page.</summary>
    [HttpGet("posts")]
    [ProducesResponseType<FeedPageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FeedPageDto>> GetPosts([FromQuery] DateTime? before = null, [FromQuery] int limit = GetFeedQuery.DefaultLimit, CancellationToken ct = default)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetFeedQuery(userId, before, limit), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("posts")]
    [EnableRateLimiting(RateLimitExtensions.Messages)]
    [ProducesResponseType<PostDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PostDto>> CreatePost(CreatePostRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CreatePostCommand(userId, request.Content, request.StickerId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>A post with every comment, oldest first.</summary>
    [HttpGet("posts/{postId:guid}")]
    [ProducesResponseType<PostThreadDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostThreadDto>> GetPost(Guid postId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetPostThreadQuery(userId, postId), ct);
        return result.IsSuccess ? Ok(result.Value) : NotFound(result.Error);
    }

    /// <summary>Deletes a post; its author or an admin can.</summary>
    [HttpDelete("posts/{postId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> DeletePost(Guid postId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeletePostCommand(userId, postId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    [HttpPost("posts/{postId:guid}/like")]
    [ProducesResponseType<FeedLikeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FeedLikeDto>> ToggleLike(Guid postId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new ToggleFeedLikeCommand(userId, postId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Comments on a post; with a parent, answers that comment (replies go one level deep).</summary>
    [HttpPost("posts/{postId:guid}/comments")]
    [EnableRateLimiting(RateLimitExtensions.Messages)]
    [ProducesResponseType<CommentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<string>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CommentDto>> CreateComment(Guid postId, CreateCommentRequest request, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new CreateCommentCommand(userId, postId, request.Content, request.ParentId, request.StickerId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("posts/{postId:guid}/comments/{commentId:guid}/like")]
    [ProducesResponseType<FeedLikeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FeedLikeDto>> ToggleCommentLike(Guid postId, Guid commentId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new ToggleFeedLikeCommand(userId, postId, commentId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Deletes a comment and its replies; its author or an admin can.</summary>
    [HttpDelete("comments/{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> DeleteComment(Guid commentId, CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new DeleteCommentCommand(userId, commentId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }

    /// <summary>The "Happening now" panel.</summary>
    [HttpGet("activity")]
    [ProducesResponseType<FeedActivityDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FeedActivityDto>> GetActivity(CancellationToken ct)
    {
        var result = await sender.Send(new GetFeedActivityQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>How many posts and replies to the caller are new since they last opened the feed.</summary>
    [HttpGet("unread")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> GetUnread(CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new GetFeedUnreadQuery(userId), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>The caller has seen the feed as it is now.</summary>
    [HttpPost("seen")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> MarkSeen(CancellationToken ct)
    {
        var userId = await userService.GetCurrentUserIdAsync(ct);
        var result = await sender.Send(new MarkFeedSeenCommand(userId), ct);
        return result.IsSuccess ? Accepted() : BadRequest(result.Error);
    }
}
