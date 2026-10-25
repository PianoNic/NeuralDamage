using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure.Services.Stickers;

namespace NeuralDamage.Infrastructure.Services.Feed;

/// <summary>A new post or comment the bots may answer, <paramref name="Depth"/> bot hops after a person.</summary>
public record FeedEvent(Guid PostId, Guid? CommentId, int Depth = 0);

public interface IFeedQueue
{
    ValueTask EnqueueAsync(FeedEvent item, CancellationToken ct = default);
}

/// <summary>
/// The bots in a feed thread, the way they are in a chat: one Jev decision
/// per new post or comment says which bots comment, like it or scroll past,
/// with the chat's thresholds, health bands, caps and fallback, plus the
/// feed's own caps that keep a thread short. Each comment a bot writes is a
/// new event of its own, which is how bots answer each other, up to
/// <see cref="FeedOptions.MaxBotChainDepth"/> hops.
/// </summary>
public class FeedConversation(
    IServiceScopeFactory scopes,
    ILogger<FeedConversation> logger,
    IOptions<BotBehaviorOptions>? behavior = null,
    IOptions<FeedOptions>? feed = null,
    BotClock? clock = null)
{
    public const string Place = "feed thread";

    /// <summary>Thread messages Jev reads before the new one.</summary>
    private const int HistoryComments = 10;

    private readonly BotBehaviorOptions _behavior = behavior?.Value ?? new();
    private readonly FeedOptions _feed = feed?.Value ?? new();
    private readonly BotClock _clock = clock ?? BotClock.Default;

    /// <summary>One bot comment saved at a time, so each one is checked against the others.</summary>
    private readonly SemaphoreSlim _saving = new(1, 1);

    /// <summary>Bots that take part in the feed: public, alive, with the feed on.</summary>
    public static bool OnFeed(Bot bot) => bot.IsActive && bot.IsPublic && bot.FeedEnabled;

    public async Task ProcessAsync(FeedEvent item, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<NeuralDamageDbContext>();

        var post = await db.Posts.Visible()
            .Include(p => p.AuthorUser)
            .Include(p => p.AuthorBot)
            .Include(p => p.Sticker)
            .Include(p => p.Image)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == item.PostId, ct);
        if (post is null)
            return;

        var comments = await db.PostComments.Visible()
            .Where(c => c.PostId == post.Id)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Include(c => c.AuthorUser)
            .Include(c => c.AuthorBot)
            .Include(c => c.Sticker)
            .AsNoTracking()
            .ToListAsync(ct);

        var subject = item.CommentId is { } commentId ? comments.FirstOrDefault(c => c.Id == commentId) : null;
        if (item.CommentId is not null && subject is null)
            return;
        var said = subject is null ? Said.Of(post) : Said.Of(subject);

        // A person wrote since this bot comment: they get answered instead.
        if (item.Depth > 0 && comments.Any(c => c.AuthorUserId != null && c.CreatedAt > said.At))
            return;

        var bots = await CandidatesAsync(db, post, comments, subject, said, ct);
        if (bots.Count == 0)
            return;

        var keyed = bots.Select((bot, i) => (Key: $"bot_{i}", Bot: bot)).ToList();
        var addressed = Addressed(said, subject, comments, bots);
        var round = new DecisionRound(
            Place,
            subject is null ? $"feed post {post.Id}" : $"feed comment {subject.Id}",
            State(post, comments, subject, said, keyed),
            FromBot: said.BotId is not null,
            addressed,
            (repliers, token) => RepliesLastMinuteAsync(db, repliers, token));

        var engine = services.GetRequiredService<IBotDecisionEngine>();
        var decided = Limit(await engine.DecideAsync(round, keyed, ct), round.Subject, post, comments, subject, addressed);

        // A like is any reaction, and a commenting bot may like as well.
        var likes = Task.WhenAll(decided
            .Where(d => d.Action == BotAction.React || d.Reacts)
            .Select(d => LikeAsync(post.Id, subject?.Id, d.Bot, ct)));

        var writers = decided.Where(d => d.Action == BotAction.Reply).Select(d => d.Bot).ToList();
        var written = await Task.WhenAll(writers.Select(bot => CommentAsync(post, subject, bot, addressed.Contains(bot.Id), ct)));
        await likes;

        if (item.Depth + 1 > _feed.MaxBotChainDepth)
            return;
        var queue = services.GetService<IFeedQueue>();
        if (queue is null)
            return;
        foreach (var comment in written.OfType<PostComment>().OrderBy(c => c.CreatedAt))
            await queue.EnqueueAsync(new FeedEvent(post.Id, comment.Id, item.Depth + 1), ct);
    }

    /// <summary>
    /// The feed's own limits on top of the chat's, so a busy post does not
    /// turn into a pile of bot comments. A bot told to comment likes instead
    /// when Jev was only lukewarm (<see cref="FeedOptions.CommentThreshold"/>),
    /// when it already commented <see cref="FeedOptions.MaxCommentsPerBot"/>
    /// times here, when the post has <see cref="FeedOptions.MaxBotCommentsPerPost"/>
    /// bot comments, or when <see cref="FeedOptions.MaxBotRepliesPerComment"/>
    /// bots already answer this one. A bot a person addressed skips them all,
    /// and a bot whose words this answers skips the per-bot cap.
    /// </summary>
    private List<BotVerdict> Limit(List<BotVerdict> decided, string subjectName, Post post, List<PostComment> comments, PostComment? subject, IReadOnlySet<Guid> addressed)
    {
        var answered = AnsweredBot(post, comments, subject);
        var perBot = comments.Where(c => c.AuthorBotId != null).GroupBy(c => c.AuthorBotId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var room = _feed.MaxBotCommentsPerPost - perBot.Values.Sum();
        var writing = 0;
        var kept = new HashSet<Guid>();

        foreach (var d in decided.Where(d => d.Action == BotAction.Reply)
            .OrderByDescending(d => addressed.Contains(d.Bot.Id))
            .ThenByDescending(d => d.Probability))
        {
            var isAddressed = addressed.Contains(d.Bot.Id);
            var reason =
                !isAddressed && d.Probability < _feed.CommentThreshold ? $"only {d.Probability:F2} sure"
                : !isAddressed && d.Bot.Id != answered && perBot.GetValueOrDefault(d.Bot.Id) >= _feed.MaxCommentsPerBot ? "its comments here are used up"
                : !isAddressed && room <= 0 ? "the post has enough bot comments"
                : !isAddressed && writing >= _feed.MaxBotRepliesPerComment ? "enough bots answer this already"
                : null;
            if (reason is not null)
            {
                logger.LogInformation("Bot {BotName} likes {Subject} instead of commenting: {Reason}", d.Bot.Name, subjectName, reason);
                continue;
            }
            kept.Add(d.Bot.Id);
            writing++;
            room--;
        }

        return decided
            .Select(d => d.Action == BotAction.Reply && !kept.Contains(d.Bot.Id) ? d with { Action = BotAction.React, Emoji = null } : d)
            .ToList();
    }

    /// <summary>
    /// The bot whose words <paramref name="subject"/> answers: the author of
    /// the comment it hangs under, or of the post for a top-level comment.
    /// None for the post itself.
    /// </summary>
    private static Guid? AnsweredBot(Post post, List<PostComment> comments, PostComment? subject) =>
        subject is null ? null
        : subject.ParentId is { } parentId ? comments.FirstOrDefault(c => c.Id == parentId)?.AuthorBotId
        : post.AuthorBotId;

    /// <summary>
    /// The bots Jev is asked about: the ones addressed, the post's author, the
    /// ones already in the thread, then others at random, up to
    /// <see cref="FeedOptions.MaxCandidates"/>. Never the author of what is answered.
    /// </summary>
    private async Task<List<Bot>> CandidatesAsync(NeuralDamageDbContext db, Post post, List<PostComment> comments, PostComment? subject, Said said, CancellationToken ct)
    {
        var all = (await db.Bots.Where(b => b.IsActive && b.IsPublic && b.FeedEnabled).AsNoTracking().ToListAsync(ct))
            .Where(b => b.Id != said.BotId)
            .ToList();
        var addressed = Addressed(said, subject, comments, all);
        var inThread = comments.Where(c => c.AuthorBotId != null).Select(c => c.AuthorBotId!.Value).Append(post.AuthorBotId ?? Guid.Empty).ToHashSet();

        return all
            .OrderByDescending(b => addressed.Contains(b.Id))
            .ThenByDescending(b => b.Id == post.AuthorBotId)
            .ThenByDescending(b => inThread.Contains(b.Id))
            .ThenBy(_ => Random.Shared.Next())
            .Take(Math.Max(1, Math.Max(_feed.MaxCandidates, addressed.Count)))
            .ToList();
    }

    /// <summary>
    /// The bots a person's comment is directed at: those it @mentions, and the
    /// bot whose comment it answers. A bot's comment addresses nobody, as in a chat.
    /// </summary>
    private static HashSet<Guid> Addressed(Said said, PostComment? subject, List<PostComment> comments, List<Bot> bots)
    {
        if (said.BotId is not null)
            return [];
        var answering = subject?.ParentId is { } parentId ? comments.FirstOrDefault(c => c.Id == parentId)?.AuthorBotId : null;
        return bots.Where(b => BotDecisionEngine.IsMentioned(said.Text, b) || b.Id == answering).Select(b => b.Id).ToHashSet();
    }

    private DecisionState State(Post post, List<PostComment> comments, PostComment? subject, Said said, List<(string Key, Bot Bot)> keyed)
    {
        var thread = new List<Said> { Said.Of(post) };
        thread.AddRange(comments.Select(Said.Of));
        var before = thread.Where(s => s.At <= said.At && s.Id != said.Id).TakeLast(HistoryComments).ToList();

        var now = DateTime.UtcNow;
        var lastHuman = thread.Where(s => s.BotId is null).Select(s => (DateTime?)s.At).Max();
        var botMessages = thread.Count(s => s.BotId is not null && (lastHuman is null || s.At > lastHuman));
        var flow = new DecisionFlow(
            botMessages,
            lastHuman is { } at ? (int)Math.Max(0, (now - at).TotalSeconds) : null,
            lastHuman >= now - TimeSpan.FromMinutes(5));

        string? replyTo = subject?.ParentId is { } parentId ? thread.FirstOrDefault(s => s.Id == parentId)?.Author
            : subject is not null ? Said.Of(post).Author
            : null;

        return new DecisionState(
            $"comments under {Said.Of(post).Author}'s post on the feed",
            before.Select(s => s.ForJev()).ToList(),
            said.ForJev() with { ReplyTo = replyTo },
            flow,
            keyed.ToDictionary(k => k.Key, k => new DecisionBot(k.Bot.Name, BotDecisionEngine.PersonaOf(k.Bot))));
    }

    /// <summary>Comments each bot wrote on the whole feed in the last minute.</summary>
    private static Task<Dictionary<Guid, int>> RepliesLastMinuteAsync(NeuralDamageDbContext db, IReadOnlyCollection<Guid> repliers, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddMinutes(-1);
        return db.PostComments
            .Where(c => c.AuthorBotId != null && repliers.Contains(c.AuthorBotId.Value) && c.CreatedAt >= since)
            .GroupBy(c => c.AuthorBotId!.Value)
            .Select(g => new { BotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.BotId, g => g.Count, ct);
    }

    private async Task LikeAsync(Guid postId, Guid? commentId, Bot bot, CancellationToken ct)
    {
        try
        {
            await Task.Delay(BotBehaviorOptions.Between(_behavior.ReactionDelayMin, _behavior.ReactionDelayMax), ct);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            if (await db.FeedLikes.AnyAsync(l => l.PostId == postId && l.CommentId == commentId && l.BotId == bot.Id, ct))
                return;
            db.FeedLikes.Add(new FeedLike { PostId = postId, CommentId = commentId, BotId = bot.Id });
            await db.SaveChangesAsync(ct);
            var count = await db.FeedLikes.CountAsync(l => l.PostId == postId && l.CommentId == commentId, ct);
            var byAuthor = commentId is { } liked
                && await db.PostComments.AnyAsync(c => c.Id == liked && c.Post.AuthorBotId == bot.Id && c.AuthorBotId != bot.Id, ct);
            await scope.ServiceProvider.GetRequiredService<IFeedNotifier>().LikeUpdated(new(postId, commentId, count, null, true, byAuthor));
            logger.LogInformation("Bot {BotName} liked {Target}", bot.Name, commentId?.ToString() ?? $"post {postId}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bot {BotName} failed to like on the feed", bot.Name);
        }
    }

    /// <summary>One bot's comment: read, type, write, post. Null when it had nothing to say or failed.</summary>
    private async Task<PostComment?> CommentAsync(Post post, PostComment? subject, Bot bot, bool addressed, CancellationToken ct)
    {
        using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task typing = Task.CompletedTask;
        try
        {
            await Task.Delay(BotBehaviorOptions.Between(_behavior.ReadDelayMin, _behavior.ReadDelayMax), ct);

            using var scope = scopes.CreateScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<NeuralDamageDbContext>();
            var notifier = services.GetRequiredService<IFeedNotifier>();
            var openRouter = services.GetRequiredService<IOpenRouterService>();
            typing = KeepTypingAsync(notifier, post.Id, bot, typingCts.Token);
            var sinceTyping = Stopwatch.StartNew();

            var comments = await db.PostComments.Visible()
                .Where(c => c.PostId == post.Id && c.CreatedAt <= (subject == null ? post.CreatedAt : subject.CreatedAt))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .Include(c => c.AuthorUser)
                .Include(c => c.AuthorBot)
                .Include(c => c.Sticker)
                .AsNoTracking()
                .ToListAsync(ct);
            var stickers = await StickerCatalog.LoadAsync(db, ct);

            var system = SystemPrompt(bot, Said.Of(post).Author, stickers.ForPrompt);
            var language = ChatLanguage.Detect(new[] { Said.Of(post) }.Concat(comments.Select(Said.Of)).Where(s => s.BotId is null).Select(s => s.Text));
            var history = History(post, comments, subject?.Id, bot.Id);
            history.Add(CommentNote(bot, language, _clock));

            var reply = await BotResponseOrchestrator.GenerateAsync(openRouter, bot.Name, bot.ModelId, bot.Temperature, system, history, logger, ct);
            var (text, sticker) = BotReplyFormatter.Single(BotReplyFormatter.ToParts(reply, bot.Name, 3, stickers.Find), PostComment.MaxContentLength);
            if (text.Length == 0 && sticker is null)
                return null;
            if (BotReplyFormatter.IsNearDuplicate(text, comments.Select(c => c.Content).Append(post.Content)))
            {
                logger.LogInformation("Bot {BotName} said what the thread already says on post {PostId}; dropping it", bot.Name, post.Id);
                return null;
            }

            var remaining = _behavior.TypingTime(text.Length) - sinceTyping.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, ct);
            await typingCts.CancelAsync();
            await typing;

            // Checked against the thread as it is now, one comment at a time:
            // the other bots of the round write in parallel and may have just
            // said the same thing, or used up the post's last comments.
            await _saving.WaitAsync(ct);
            PostComment comment;
            try
            {
                var thread = await db.PostComments.Visible()
                    .Where(c => c.PostId == post.Id)
                    .Select(c => new { c.Content, c.AuthorBotId })
                    .ToListAsync(ct);
                if (BotReplyFormatter.IsNearDuplicate(text, thread.Select(c => c.Content).Append(post.Content)))
                {
                    logger.LogInformation("Bot {BotName} said what the thread already says on post {PostId}; dropping it", bot.Name, post.Id);
                    return null;
                }
                if (!addressed && thread.Count(c => c.AuthorBotId != null) >= _feed.MaxBotCommentsPerPost)
                {
                    logger.LogInformation("Post {PostId} filled up with bot comments while {BotName} wrote; dropping it", post.Id, bot.Name);
                    return null;
                }

                comment = new PostComment
                {
                    PostId = post.Id,
                    ParentId = subject is null ? null : subject.ParentId ?? subject.Id,
                    AuthorBotId = bot.Id,
                    Content = text,
                    StickerId = sticker?.Id,
                };
                db.PostComments.Add(comment);
                if (sticker is not null && await db.Stickers.FirstOrDefaultAsync(s => s.Id == sticker.Id, ct) is { } used)
                    used.UseCount++;
                await db.SaveChangesAsync(ct);
            }
            finally
            {
                _saving.Release();
            }

            if (await FeedReads.CommentAsync(db, comment.Id, null, ct) is { } dto)
                await notifier.CommentNew(dto);
            await FeedNotices.NotifyRepliesAsync(db, notifier, comment, ct, answeredUserId: subject?.AuthorUserId);
            logger.LogInformation("Bot {BotName} commented on post {PostId}", bot.Name, post.Id);
            return comment;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bot {BotName} failed to comment on post {PostId}", bot.Name, post.Id);
            return null;
        }
        finally
        {
            await typingCts.CancelAsync();
            await typing;
        }
    }

    public static string SystemPrompt(Bot bot, string postAuthor, IReadOnlyList<BotSticker>? stickers) =>
        BotPromptBuilder.WithStickers($"""
            {BotPromptBuilder.Character(bot)}

            You are "{bot.Name}", on the feed of a chat app: a public timeline everyone using the app reads. You are in the comments under a post by {postAuthor}.

            {BotPromptBuilder.HouseRules(bot.Name)}

            {CommentRules}
            """, stickers);

    /// <summary>How a comment differs from a chat message: shorter, about the post, in the bot's own voice.</summary>
    public const string CommentRules = """
        Commenting on the feed:
        - A comment is usually one short line, like a real comment under a post. Go longer only rarely.
        - React to a concrete detail of the post, or of the comment you're answering: a joke about it, your take on it, or disagree with it.
        - Don't fall back on your catchphrases or slogans. Use one only when it really fits this exact moment.
        - Don't echo or paraphrase what another comment already said. If you have nothing new to add, say something else or keep it very short.
        - Don't copy how the others write, such as ALL CAPS, their phrasing or punctuation. Keep your own voice.
        """;

    /// <summary>The note after a thread's history: the time, who the bot is, and to write one short comment.</summary>
    public static ChatMessage CommentNote(Bot bot, PeopleLanguage language, BotClock? clock = null) =>
        BotPromptBuilder.BuildNote(clock: clock, bot: bot, language: language,
            instruction: "Write one comment for this thread: usually one short line that reacts to something specific in what you're answering. Skip your catchphrases, don't repeat what others said, and keep your own voice.");

    /// <summary>
    /// The thread as the model reads it: the post first, then the comments in
    /// order, the bot's own as its turns. Who a reply answers and the comment
    /// being answered are marked as in a chat.
    /// </summary>
    public static List<ChatMessage> History(Post post, List<PostComment> comments, Guid? answeringId, Guid botId)
    {
        var byId = comments.ToDictionary(c => c.Id);
        var postSaid = Said.Of(post);
        var history = new List<ChatMessage>
        {
            post.AuthorBotId == botId
                ? new ChatMessage("assistant", postSaid.Text)
                : new ChatMessage("user", $"[{postSaid.Author}] (post){(answeringId is null ? " (you're answering this)" : "")}: {postSaid.Text}"),
        };
        foreach (var c in comments)
        {
            var said = Said.Of(c);
            if (c.AuthorBotId == botId)
            {
                history.Add(new ChatMessage("assistant", said.Text));
                continue;
            }
            var to = c.ParentId is { } p && byId.TryGetValue(p, out var parent) ? $" (→ {Said.Of(parent).Author})" : "";
            var mark = c.Id == answeringId ? " (you're answering this)" : "";
            history.Add(new ChatMessage("user", $"[{said.Author}]{to}{mark}: {said.Text}"));
        }
        return history;
    }

    private async Task KeepTypingAsync(IFeedNotifier notifier, Guid postId, Bot bot, CancellationToken ct)
    {
        try
        {
            do
            {
                await notifier.Typing(postId, bot.Id, bot.Name, true);
                if (_behavior.TypingHeartbeat <= TimeSpan.Zero)
                    return;
                await Task.Delay(_behavior.TypingHeartbeat, ct);
            }
            while (!ct.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send feed typing for bot {BotName}", bot.Name);
        }
    }

    /// <summary>A post or comment, read the same way for Jev and the model.</summary>
    private sealed record Said(Guid Id, string Author, Guid? BotId, string Text, DateTime At)
    {
        public static Said Of(Post p) => new(p.Id, FeedReads.Author(p.AuthorUser, p.AuthorBot).Name, p.AuthorBotId, WithSticker(FeedReads.WithPicture(p.Content, p.Image), p.Sticker, FeedReads.Author(p.AuthorUser, p.AuthorBot).Name), p.CreatedAt);

        public static Said Of(PostComment c) => new(c.Id, FeedReads.Author(c.AuthorUser, c.AuthorBot).Name, c.AuthorBotId, WithSticker(c.Content, c.Sticker, FeedReads.Author(c.AuthorUser, c.AuthorBot).Name), c.CreatedAt);

        public DecisionMessage ForJev() => new(Author, BotId is not null, BotDecisionEngine.ForJev(Text));

        private static string WithSticker(string content, Sticker? sticker, string author) =>
            sticker is null ? content : string.Join("\n", new[] { content, BotPromptBuilder.StickerLine(author, sticker) }.Where(s => s.Length > 0));
    }
}
