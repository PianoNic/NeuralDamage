using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Stories;

namespace NeuralDamage.Application.Queries;

/// <summary>A page of the feed, newest first, older than <paramref name="Before"/> when it is set.</summary>
public record GetFeedQuery(Guid UserId, DateTime? Before = null, int Limit = GetFeedQuery.DefaultLimit) : IQuery<Result<FeedPageDto>>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
}

public class GetFeedHandler(NeuralDamageDbContext db, AppAdmins admins) : IQueryHandler<GetFeedQuery, Result<FeedPageDto>>
{
    public async ValueTask<Result<FeedPageDto>> Handle(GetFeedQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, GetFeedQuery.MaxLimit);
        var query = db.Posts.Visible();
        if (request.Before is { } before)
            query = query.Where(p => p.CreatedAt < before);
        var ids = await query.OrderByDescending(p => p.CreatedAt).Take(limit).Select(p => p.Id).ToListAsync(cancellationToken);

        var posts = await FeedReads.PostsAsync(db, ids, request.UserId, ct: cancellationToken);
        return Result<FeedPageDto>.Success(new FeedPageDto(posts, await Admins.IsAdminAsync(db, admins, request.UserId, cancellationToken)));
    }
}

public record GetPostThreadQuery(Guid UserId, Guid PostId) : IQuery<Result<PostThreadDto>>;

public class GetPostThreadHandler(NeuralDamageDbContext db, AppAdmins admins) : IQueryHandler<GetPostThreadQuery, Result<PostThreadDto>>
{
    public async ValueTask<Result<PostThreadDto>> Handle(GetPostThreadQuery request, CancellationToken cancellationToken)
    {
        var post = (await FeedReads.PostsAsync(db, [request.PostId], request.UserId, previewComments: 0, cancellationToken)).FirstOrDefault();
        if (post is null || !await db.Posts.Visible().AnyAsync(p => p.Id == request.PostId, cancellationToken))
            return Result<PostThreadDto>.Failure("That post is gone.");

        var ids = await db.PostComments.Visible().Where(c => c.PostId == request.PostId).Select(c => c.Id).ToListAsync(cancellationToken);
        var comments = await FeedReads.CommentsAsync(db, ids, request.UserId, cancellationToken);
        return Result<PostThreadDto>.Success(new PostThreadDto(post, comments, await Admins.IsAdminAsync(db, admins, request.UserId, cancellationToken)));
    }
}

/// <summary>
/// What is new on the feed for the user: posts by others since they last
/// opened it, plus comments answering their posts or comments. A user who
/// never opened it counts the last day.
/// </summary>
public record GetFeedUnreadQuery(Guid UserId) : IQuery<Result<int>>;

public class GetFeedUnreadHandler(NeuralDamageDbContext db) : IQueryHandler<GetFeedUnreadQuery, Result<int>>
{
    public async ValueTask<Result<int>> Handle(GetFeedUnreadQuery request, CancellationToken cancellationToken)
    {
        var seen = await db.Users.Where(u => u.Id == request.UserId).Select(u => u.FeedSeenAt).FirstOrDefaultAsync(cancellationToken)
            ?? DateTime.UtcNow.AddDays(-1);
        var posts = await db.Posts.Visible().CountAsync(p => p.CreatedAt > seen && p.AuthorUserId != request.UserId, cancellationToken);
        var replies = await db.PostComments.Visible().CountAsync(c => c.CreatedAt > seen && c.AuthorUserId != request.UserId
            && (c.Post.AuthorUserId == request.UserId || (c.Parent != null && c.Parent.AuthorUserId == request.UserId)), cancellationToken);
        return Result<int>.Success(posts + replies);
    }
}

/// <summary>The "Happening now" panel: the latest goings-on and today's counts, in the bots' time zone.</summary>
public record GetFeedActivityQuery : IQuery<Result<FeedActivityDto>>
{
    public const int Items = 6;
}

public class GetFeedActivityHandler(NeuralDamageDbContext db, BotClock clock) : IQueryHandler<GetFeedActivityQuery, Result<FeedActivityDto>>
{
    public async ValueTask<Result<FeedActivityDto>> Handle(GetFeedActivityQuery request, CancellationToken cancellationToken)
    {
        var startOfDay = Infrastructure.Services.Feed.FeedSchedule.StartOfDay(DateTimeOffset.UtcNow, clock);

        var posts = await db.Posts.Visible()
            .OrderByDescending(p => p.CreatedAt).Take(GetFeedActivityQuery.Items)
            .Select(p => new FeedActivityItemDto("post",
                p.AuthorBot != null ? p.AuthorBot.Name : p.AuthorUser!.DisplayName != "" ? p.AuthorUser.DisplayName : p.AuthorUser.Email,
                p.AuthorBotId != null, p.Id, p.Content, p.CreatedAt))
            .ToListAsync(cancellationToken);
        var comments = await db.PostComments.Visible()
            .OrderByDescending(c => c.CreatedAt).Take(GetFeedActivityQuery.Items)
            .Select(c => new FeedActivityItemDto("comment",
                c.AuthorBot != null ? c.AuthorBot.Name : c.AuthorUser!.DisplayName != "" ? c.AuthorUser.DisplayName : c.AuthorUser.Email,
                c.AuthorBotId != null, c.PostId,
                c.Post.AuthorBot != null ? c.Post.AuthorBot.Name : c.Post.AuthorUser!.DisplayName != "" ? c.Post.AuthorUser.DisplayName : c.Post.AuthorUser.Email,
                c.CreatedAt))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var stories = await db.Stories.Live(now)
            .OrderByDescending(s => s.CreatedAt).Take(GetFeedActivityQuery.Items)
            .Select(s => new FeedActivityItemDto("story",
                s.AuthorBot != null ? s.AuthorBot.Name : s.AuthorUser!.DisplayName != "" ? s.AuthorUser.DisplayName : s.AuthorUser.Email,
                s.AuthorBotId != null, null, null, s.CreatedAt))
            .ToListAsync(cancellationToken);

        var recent = posts.Concat(comments).Concat(stories).OrderByDescending(i => i.At).Take(GetFeedActivityQuery.Items).ToList();
        var postsToday = await db.Posts.Visible().CountAsync(p => p.CreatedAt >= startOfDay, cancellationToken);
        var botComments = await db.PostComments.Visible().CountAsync(c => c.AuthorBotId != null && c.CreatedAt >= startOfDay, cancellationToken);
        var storiesLive = await db.Stories.Live(now).CountAsync(cancellationToken);
        return Result<FeedActivityDto>.Success(new FeedActivityDto(recent, postsToday, botComments, storiesLive));
    }
}
