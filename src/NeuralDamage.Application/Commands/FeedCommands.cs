using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Feed;

namespace NeuralDamage.Application.Commands;

/// <param name="ImageId">A picture the person had generated for this post.</param>
public record CreatePostCommand(Guid UserId, string Content, Guid? StickerId = null, Guid? ImageId = null) : ICommand<Result<PostDto>>;

public class CreatePostHandler(NeuralDamageDbContext db, IFeedNotifier notifier, IFeedQueue queue) : ICommandHandler<CreatePostCommand, Result<PostDto>>
{
    public async ValueTask<Result<PostDto>> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
        var content = request.Content.Trim();
        if (content.Length == 0 && request.StickerId is null && request.ImageId is null)
            return Result<PostDto>.Failure("Write something to post.");
        if (content.Length > Post.MaxContentLength)
            return Result<PostDto>.Failure($"A post can be at most {Post.MaxContentLength} characters.");
        if (await FeedStickers.UseAsync(db, request.StickerId, cancellationToken) is { } problem)
            return Result<PostDto>.Failure(problem);

        if (request.ImageId is { } imageId)
        {
            var image = await db.GeneratedImages.FirstOrDefaultAsync(i => i.Id == imageId && i.UserId == request.UserId, cancellationToken);
            if (image is null)
                return Result<PostDto>.Failure("That picture is gone.");
            if (image.Status != GeneratedImageStatus.Ready)
                return Result<PostDto>.Failure("That picture is not ready yet.");
            if (await db.Posts.AnyAsync(p => p.ImageId == imageId, cancellationToken))
                return Result<PostDto>.Failure("That picture is already posted.");
        }

        var post = new Post { AuthorUserId = request.UserId, Content = content, StickerId = request.StickerId, ImageId = request.ImageId };
        db.Posts.Add(post);
        await db.SaveChangesAsync(cancellationToken);

        var dto = (await FeedReads.PostAsync(db, post.Id, request.UserId, cancellationToken))!;
        await notifier.PostNew(dto with { LikedByMe = false });
        await queue.EnqueueAsync(new FeedEvent(post.Id, null), cancellationToken);
        return Result<PostDto>.Success(dto);
    }
}

public record CreateCommentCommand(Guid UserId, Guid PostId, string Content, Guid? ParentId = null, Guid? StickerId = null) : ICommand<Result<CommentDto>>;

public class CreateCommentHandler(NeuralDamageDbContext db, IFeedNotifier notifier, IFeedQueue queue) : ICommandHandler<CreateCommentCommand, Result<CommentDto>>
{
    public async ValueTask<Result<CommentDto>> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        var content = request.Content.Trim();
        if (content.Length == 0 && request.StickerId is null)
            return Result<CommentDto>.Failure("Write something to comment.");
        if (content.Length > PostComment.MaxContentLength)
            return Result<CommentDto>.Failure($"A comment can be at most {PostComment.MaxContentLength} characters.");
        if (!await db.Posts.Visible().AnyAsync(p => p.Id == request.PostId, cancellationToken))
            return Result<CommentDto>.Failure("That post is gone.");

        // Replies go one level deep: a reply to a reply hangs under its top-level comment.
        Guid? parentId = null;
        Guid? answeredUserId = null;
        if (request.ParentId is { } answering)
        {
            var parent = await db.PostComments.Where(c => c.Id == answering && c.PostId == request.PostId)
                .Select(c => new { c.Id, c.ParentId, c.AuthorUserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (parent is null)
                return Result<CommentDto>.Failure("The comment you answered is gone.");
            parentId = parent.ParentId ?? parent.Id;
            answeredUserId = parent.AuthorUserId;
        }
        if (await FeedStickers.UseAsync(db, request.StickerId, cancellationToken) is { } problem)
            return Result<CommentDto>.Failure(problem);

        var comment = new PostComment
        {
            PostId = request.PostId,
            ParentId = parentId,
            AuthorUserId = request.UserId,
            Content = content,
            StickerId = request.StickerId,
        };
        db.PostComments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        var dto = (await FeedReads.CommentAsync(db, comment.Id, request.UserId, cancellationToken))!;
        await notifier.CommentNew(dto);
        await FeedNotices.NotifyRepliesAsync(db, notifier, comment, cancellationToken, answeredUserId);
        await queue.EnqueueAsync(new FeedEvent(request.PostId, comment.Id), cancellationToken);
        return Result<CommentDto>.Success(dto);
    }
}

public record DeletePostCommand(Guid UserId, Guid PostId) : ICommand<Result>;

public class DeletePostHandler(NeuralDamageDbContext db, AppAdmins admins, IFeedNotifier notifier, IAttachmentStorage storage) : ICommandHandler<DeletePostCommand, Result>
{
    public async ValueTask<Result> Handle(DeletePostCommand request, CancellationToken cancellationToken)
    {
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == request.PostId, cancellationToken);
        if (post is null)
            return Result.Failure("That post is gone.");
        if (post.AuthorUserId != request.UserId && !await Admins.IsAdminAsync(db, admins, request.UserId, cancellationToken))
            return Result.Failure("Only its author or an admin can delete a post.");

        // Its picture goes with it.
        var image = post.ImageId is { } imageId ? await db.GeneratedImages.FirstOrDefaultAsync(i => i.Id == imageId, cancellationToken) : null;
        db.Posts.Remove(post);
        if (image is not null)
            db.GeneratedImages.Remove(image);
        await db.SaveChangesAsync(cancellationToken);
        if (image is not null)
            storage.DeleteGeneratedImage(image.Id);
        await notifier.PostDeleted(post.Id);
        return Result.Success();
    }
}

public record DeleteCommentCommand(Guid UserId, Guid CommentId) : ICommand<Result>;

public class DeleteCommentHandler(NeuralDamageDbContext db, AppAdmins admins, IFeedNotifier notifier) : ICommandHandler<DeleteCommentCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteCommentCommand request, CancellationToken cancellationToken)
    {
        var comment = await db.PostComments.FirstOrDefaultAsync(c => c.Id == request.CommentId, cancellationToken);
        if (comment is null)
            return Result.Failure("That comment is gone.");
        if (comment.AuthorUserId != request.UserId && !await Admins.IsAdminAsync(db, admins, request.UserId, cancellationToken))
            return Result.Failure("Only its author or an admin can delete a comment.");

        // A top-level comment takes its replies with it.
        var replies = await db.PostComments.Where(c => c.ParentId == comment.Id).ToListAsync(cancellationToken);
        db.PostComments.RemoveRange(replies);
        db.PostComments.Remove(comment);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var reply in replies)
            await notifier.CommentDeleted(comment.PostId, reply.Id);
        await notifier.CommentDeleted(comment.PostId, comment.Id);
        return Result.Success();
    }
}

/// <summary>Likes or unlikes a post (no <paramref name="CommentId"/>) or one of its comments.</summary>
public record ToggleFeedLikeCommand(Guid UserId, Guid PostId, Guid? CommentId = null) : ICommand<Result<FeedLikeDto>>;

public class ToggleFeedLikeHandler(NeuralDamageDbContext db, IFeedNotifier notifier) : ICommandHandler<ToggleFeedLikeCommand, Result<FeedLikeDto>>
{
    public async ValueTask<Result<FeedLikeDto>> Handle(ToggleFeedLikeCommand request, CancellationToken cancellationToken)
    {
        var exists = request.CommentId is { } commentId
            ? await db.PostComments.AnyAsync(c => c.Id == commentId && c.PostId == request.PostId, cancellationToken)
            : await db.Posts.Visible().AnyAsync(p => p.Id == request.PostId, cancellationToken);
        if (!exists)
            return Result<FeedLikeDto>.Failure("That is gone.");

        var like = await db.FeedLikes.FirstOrDefaultAsync(l => l.PostId == request.PostId && l.CommentId == request.CommentId && l.UserId == request.UserId, cancellationToken);
        if (like is null)
            db.FeedLikes.Add(new FeedLike { PostId = request.PostId, CommentId = request.CommentId, UserId = request.UserId });
        else
            db.FeedLikes.Remove(like);
        await db.SaveChangesAsync(cancellationToken);

        var count = await db.FeedLikes.CountAsync(l => l.PostId == request.PostId && l.CommentId == request.CommentId, cancellationToken);
        var dto = new FeedLikeDto(request.PostId, request.CommentId, count, request.UserId, like is null);
        await notifier.LikeUpdated(dto);
        return Result<FeedLikeDto>.Success(dto);
    }
}

public record MarkFeedSeenCommand(Guid UserId) : ICommand<Result>;

public class MarkFeedSeenHandler(NeuralDamageDbContext db) : ICommandHandler<MarkFeedSeenCommand, Result>
{
    public async ValueTask<Result> Handle(MarkFeedSeenCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure("User not found.");
        user.FeedSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class FeedStickers
{
    /// <summary>Counts a use of the sticker; a message when there is no such sticker any more.</summary>
    public static async Task<string?> UseAsync(NeuralDamageDbContext db, Guid? stickerId, CancellationToken ct)
    {
        if (stickerId is null)
            return null;
        var sticker = await db.Stickers.FirstOrDefaultAsync(s => s.Id == stickerId && s.DeletedAt == null, ct);
        if (sticker is null)
            return "That sticker is gone.";
        sticker.UseCount++;
        return null;
    }
}
