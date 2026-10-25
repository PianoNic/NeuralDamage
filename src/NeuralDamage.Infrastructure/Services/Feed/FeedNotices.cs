using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;

namespace NeuralDamage.Infrastructure.Services.Feed;

/// <summary>
/// Who hears about a new comment: the person whose post it is under, the person whose comment it
/// answers, and anyone it @mentions by name or nickname. A new post tells the people it mentions.
/// </summary>
public static class FeedNotices
{
    /// <param name="answeredUserId">
    /// The person whose comment this one answers when that is not its parent:
    /// replies go one level deep, so a bot answering a reply hangs under the
    /// top-level comment but still answers the reply's author.
    /// </param>
    public static async Task NotifyRepliesAsync(NeuralDamageDbContext db, IFeedNotifier notifier, PostComment comment, CancellationToken ct = default, Guid? answeredUserId = null)
    {
        var postAuthor = await db.Posts.Where(p => p.Id == comment.PostId).Select(p => p.AuthorUserId).FirstOrDefaultAsync(ct);
        var parentAuthor = comment.ParentId is { } parentId
            ? await db.PostComments.Where(c => c.Id == parentId).Select(c => c.AuthorUserId).FirstOrDefaultAsync(ct)
            : null;

        var author = comment.AuthorBot is { } bot ? bot.Name
            : comment.AuthorUser is { } user ? FeedReads.Author(user, null).Name
            : await AuthorNameAsync(db, comment, ct);

        var told = new HashSet<Guid>();
        if (postAuthor is { } owner && owner != comment.AuthorUserId && told.Add(owner))
            await notifier.Reply(owner, new FeedReplyNoticeDto(comment.PostId, comment.Id, author, Preview(comment), OnYourPost: true));
        foreach (var answered in new[] { answeredUserId, parentAuthor })
            if (answered is { } id && id != comment.AuthorUserId && told.Add(id))
                await notifier.Reply(id, new FeedReplyNoticeDto(comment.PostId, comment.Id, author, Preview(comment), OnYourPost: false));
        foreach (var id in await PeopleNames.MentionedAsync(db, comment.Content, ct))
            if (id != comment.AuthorUserId && told.Add(id))
                await notifier.Reply(id, new FeedReplyNoticeDto(comment.PostId, comment.Id, author, Preview(comment), OnYourPost: false, Mention: true));
    }

    /// <summary>Tells the people a new post @mentions, by name or nickname, all but its author.</summary>
    public static async Task NotifyMentionsAsync(NeuralDamageDbContext db, IFeedNotifier notifier, Post post, string authorName, CancellationToken ct = default)
    {
        foreach (var id in await PeopleNames.MentionedAsync(db, post.Content, ct))
            if (id != post.AuthorUserId)
                await notifier.Reply(id, new FeedReplyNoticeDto(post.Id, null, authorName, Preview(post.Content), OnYourPost: false, Mention: true));
    }

    private static async Task<string> AuthorNameAsync(NeuralDamageDbContext db, PostComment comment, CancellationToken ct)
    {
        if (comment.AuthorBotId is { } botId)
            return await db.Bots.Where(b => b.Id == botId).Select(b => b.Name).FirstOrDefaultAsync(ct) ?? "A bot";
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == comment.AuthorUserId, ct);
        return FeedReads.Author(user, null).Name;
    }

    private static string Preview(PostComment comment) => Preview(comment.Content);

    private static string Preview(string content) =>
        content.Length == 0 ? "Sticker" : content.Length > 140 ? content[..140] + "..." : content;
}
