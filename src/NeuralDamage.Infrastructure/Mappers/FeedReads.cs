using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;

namespace NeuralDamage.Infrastructure.Mappers;

/// <summary>
/// The feed as the API hands it out, for the endpoints and for the live
/// updates alike, so both read the same: who can be seen, and how a post or
/// comment looks with its counts.
/// </summary>
public static class FeedReads
{
    /// <summary>
    /// Everyone signed in sees people's posts and public bots' posts. A private
    /// bot never posts, and a deleted bot's posts go with it.
    /// </summary>
    public static IQueryable<Post> Visible(this IQueryable<Post> posts) =>
        posts.Where(p => p.AuthorUserId != null || (p.AuthorBot != null && p.AuthorBot.IsPublic && p.AuthorBot.IsActive));

    public static IQueryable<PostComment> Visible(this IQueryable<PostComment> comments) =>
        comments.Where(c => c.AuthorUserId != null || (c.AuthorBot != null && c.AuthorBot.IsPublic && c.AuthorBot.IsActive));

    public static FeedAuthorDto Author(User? user, Bot? bot) =>
        bot is not null
            ? new FeedAuthorDto(bot.Id, bot.Name, bot.AvatarUrl, true, bot.ModelId)
            : new FeedAuthorDto(user?.Id ?? Guid.Empty, user is null ? "Unknown" : user.DisplayName.Length > 0 ? user.DisplayName : user.Email, user?.AvatarUrl, false, null);

    /// <summary>The posts with these ids, in the order given, as <paramref name="viewerId"/> sees them.</summary>
    public static async Task<List<PostDto>> PostsAsync(NeuralDamageDbContext db, IReadOnlyList<Guid> ids, Guid? viewerId, int previewComments = 2, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        var posts = await db.Posts
            .Where(p => ids.Contains(p.Id))
            .Include(p => p.AuthorUser)
            .Include(p => p.AuthorBot)
            .Include(p => p.Sticker)
            .Include(p => p.Image)
            .AsNoTracking()
            .ToListAsync(ct);

        var likes = await db.FeedLikes
            .Where(l => ids.Contains(l.PostId) && l.CommentId == null)
            .GroupBy(l => l.PostId)
            .Select(g => new { PostId = g.Key, Count = g.Count(), Mine = g.Any(l => viewerId != null && l.UserId == viewerId) })
            .ToDictionaryAsync(g => g.PostId, ct);

        var comments = await db.PostComments.Visible()
            .Where(c => ids.Contains(c.PostId))
            .GroupBy(c => c.PostId)
            .Select(g => new { PostId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PostId, g => g.Count, ct);

        var previews = new Dictionary<Guid, List<CommentDto>>();
        if (previewComments > 0)
        {
            // The newest top-level comments of every post, a few each: only ids and times are read for all of them.
            var latest = (await db.PostComments.Visible()
                    .Where(c => ids.Contains(c.PostId) && c.ParentId == null)
                    .Select(c => new { c.Id, c.PostId, c.CreatedAt })
                    .ToListAsync(ct))
                .GroupBy(c => c.PostId)
                .SelectMany(g => g.OrderByDescending(c => c.CreatedAt).Take(previewComments).Select(c => c.Id))
                .ToList();
            previews = (await CommentsAsync(db, latest, viewerId, ct))
                .GroupBy(c => c.PostId)
                .ToDictionary(g => g.Key, g => g.OrderBy(c => c.CreatedAt).ToList());
        }

        var byId = posts.ToDictionary(p => p.Id);
        return ids.Where(byId.ContainsKey).Select(id =>
        {
            var p = byId[id];
            var like = likes.GetValueOrDefault(id);
            return new PostDto(
                p.Id,
                Author(p.AuthorUser, p.AuthorBot),
                p.Content,
                p.Sticker?.ToMessageDto(),
                p.SourceChatId,
                p.CreatedAt,
                like?.Count ?? 0,
                like?.Mine ?? false,
                comments.GetValueOrDefault(id),
                previews.GetValueOrDefault(id) ?? [],
                ImageOf(p.Image));
        }).ToList();
    }

    /// <summary>A post's picture, when it has a ready one.</summary>
    public static FeedImageDto? ImageOf(GeneratedImage? image) =>
        image is { Status: GeneratedImageStatus.Ready }
            ? new FeedImageDto(image.Id, ImageUrl(image.Id), image.Width, image.Height, image.Prompt, image.Kind, image.SourceUrl)
            : null;

    public static string ImageUrl(Guid imageId) => $"/api/images/{imageId}/file";

    /// <summary>A post's text with its picture as what it shows, for the models that read the feed.</summary>
    public static string WithPicture(string content, GeneratedImage? image) =>
        image is { Status: GeneratedImageStatus.Ready }
            ? string.Join("\n", new[] { content, $"[a picture: {image.Prompt}]" }.Where(s => s.Length > 0))
            : content;

    public static async Task<PostDto?> PostAsync(NeuralDamageDbContext db, Guid id, Guid? viewerId, CancellationToken ct = default) =>
        (await PostsAsync(db, [id], viewerId, ct: ct)).FirstOrDefault();

    /// <summary>The comments with these ids, oldest first.</summary>
    public static async Task<List<CommentDto>> CommentsAsync(NeuralDamageDbContext db, IReadOnlyCollection<Guid> ids, Guid? viewerId, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        var comments = await db.PostComments
            .Where(c => ids.Contains(c.Id))
            .Include(c => c.AuthorUser)
            .Include(c => c.AuthorBot)
            .Include(c => c.Sticker)
            .AsNoTracking()
            .ToListAsync(ct);

        var likes = await db.FeedLikes
            .Where(l => l.CommentId != null && ids.Contains(l.CommentId.Value))
            .GroupBy(l => l.CommentId!.Value)
            .Select(g => new { CommentId = g.Key, Count = g.Count(), Mine = g.Any(l => viewerId != null && l.UserId == viewerId) })
            .ToDictionaryAsync(g => g.CommentId, ct);

        // The post's author liking a comment, as Instagram's "liked by the creator"; their own comments don't count.
        var authorLiked = (await db.FeedLikes
            .Where(l => l.CommentId != null && ids.Contains(l.CommentId.Value))
            .Where(l => (l.UserId != null && l.UserId == l.Post.AuthorUserId && l.Comment!.AuthorUserId != l.UserId)
                || (l.BotId != null && l.BotId == l.Post.AuthorBotId && l.Comment!.AuthorBotId != l.BotId))
            .Select(l => l.CommentId!.Value)
            .ToListAsync(ct)).ToHashSet();

        return comments
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => new CommentDto(
                c.Id,
                c.PostId,
                c.ParentId,
                Author(c.AuthorUser, c.AuthorBot),
                c.Content,
                c.Sticker?.ToMessageDto(),
                c.CreatedAt,
                likes.GetValueOrDefault(c.Id)?.Count ?? 0,
                likes.GetValueOrDefault(c.Id)?.Mine ?? false,
                authorLiked.Contains(c.Id)))
            .ToList();
    }

    public static async Task<CommentDto?> CommentAsync(NeuralDamageDbContext db, Guid id, Guid? viewerId, CancellationToken ct = default) =>
        (await CommentsAsync(db, [id], viewerId, ct)).FirstOrDefault();
}
