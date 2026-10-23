using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Stories;

namespace NeuralDamage.Infrastructure.BackgroundServices;

/// <summary>What one scheduler tick made.</summary>
public record FeedTick(List<Post> Posts, List<StoryDto> Stories);

public class FeedQueue : IFeedQueue
{
    private readonly Channel<FeedEvent> _channel = Channel.CreateUnbounded<FeedEvent>();

    public ChannelReader<FeedEvent> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(FeedEvent item, CancellationToken ct = default) => _channel.Writer.WriteAsync(item, ct);
}

/// <summary>
/// Hands each post's events to a worker of its own: within a thread they are
/// answered in order, across threads at the same time, as chats are.
/// </summary>
public class FeedBackgroundService(FeedQueue queue, FeedConversation conversation, ILogger<FeedBackgroundService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, Channel<FeedEvent>> _threads = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            var thread = _threads.GetOrAdd(item.PostId, postId =>
            {
                var channel = Channel.CreateUnbounded<FeedEvent>(new UnboundedChannelOptions { SingleReader = true });
                _ = Task.Run(() => RunAsync(channel.Reader, stoppingToken), stoppingToken);
                return channel;
            });
            thread.Writer.TryWrite(item);
        }
    }

    private async Task RunAsync(ChannelReader<FeedEvent> reader, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await conversation.ProcessAsync(item, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Feed round failed for post {PostId}", item.PostId);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}

/// <summary>
/// Lets the bots post on their own: every tick, each bot that is due
/// (<see cref="FeedSchedule.IsDue"/>) may write a post, and the other bots
/// get to react to it like to any new post.
/// </summary>
public class FeedScheduler(
    IServiceScopeFactory scopes,
    ILogger<FeedScheduler> logger,
    IOptions<FeedOptions>? options = null,
    BotClock? clock = null,
    TimeProvider? time = null) : BackgroundService
{
    private readonly FeedOptions _options = options?.Value ?? new();
    private readonly BotClock _clock = clock ?? BotClock.Default;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Tick <= TimeSpan.Zero)
            return;
        using var timer = new PeriodicTimer(_options.Tick, _time);
        try
        {
            if (_options.StartDelay > TimeSpan.Zero)
                await Task.Delay(_options.StartDelay, _time, stoppingToken);
            do
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Feed scheduler tick failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>One tick: the bots that are due, at most <see cref="FeedOptions.MaxPostsPerTick"/> of them, post.</summary>
    /// <returns>The posts and stories made.</returns>
    public async Task<FeedTick> TickAsync(CancellationToken ct = default, Func<double>? random = null)
    {
        if (!_options.BotsPost)
            return new FeedTick([], []);
        random ??= Random.Shared.NextDouble;

        var now = _time.GetUtcNow();
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
        var startOfDay = FeedSchedule.StartOfDay(now, _clock);

        var bots = await db.Bots
            .Where(b => b.IsActive && b.IsPublic && b.FeedEnabled && b.MaxPostsPerDay > 0)
            .AsNoTracking()
            .ToListAsync(ct);
        var stats = await db.Posts
            .Where(p => p.AuthorBotId != null)
            .GroupBy(p => p.AuthorBotId!.Value)
            .Select(g => new { BotId = g.Key, Today = g.Count(p => p.CreatedAt >= startOfDay), Last = g.Max(p => p.CreatedAt) })
            .ToDictionaryAsync(g => g.BotId, ct);

        var stories = await db.Stories
            .Where(s => s.AuthorBotId != null && s.CreatedAt >= startOfDay)
            .GroupBy(s => s.AuthorBotId!.Value)
            .Select(g => new { BotId = g.Key, Today = g.Count(), Last = g.Max(s => s.CreatedAt) })
            .ToDictionaryAsync(g => g.BotId, ct);

        // Spacing counts stories too: a bot that just made one waits before posting.
        DateTime? Last(Guid botId) =>
            new[] { stats.GetValueOrDefault(botId)?.Last, stories.GetValueOrDefault(botId)?.Last }.Max();

        var due = bots
            .Where(b => FeedSchedule.IsDue(b, stats.GetValueOrDefault(b.Id)?.Today ?? 0, Last(b.Id), now, _clock, _options, random()))
            .OrderBy(_ => random())
            .Take(Math.Max(0, _options.MaxPostsPerTick))
            .ToList();

        var tick = new FeedTick([], []);
        foreach (var bot in due)
        {
            // Now and then a story instead of a post, within the bot's own cap for stories.
            if ((stories.GetValueOrDefault(bot.Id)?.Today ?? 0) < bot.MaxStoriesPerDay && random() < _options.StoryShare)
            {
                if (await StoryAsync(bot, now, ct) is { } story)
                    tick.Stories.Add(story);
            }
            else if (await PostAsync(bot, now, random(), ct) is { } post)
            {
                tick.Posts.Add(post);
            }
        }
        return tick;
    }

    /// <summary>Has <paramref name="bot"/> make a story now; null when its spec was dropped.</summary>
    public async Task<StoryDto?> StoryAsync(Bot bot, DateTimeOffset now, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<StoryWriter>().WriteAsync(bot, now, ct);
    }

    /// <summary>Writes and publishes one post by <paramref name="bot"/> now, whatever the schedule says.</summary>
    public async Task<Post?> PostAsync(Bot bot, DateTimeOffset now, double roll, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<NeuralDamageDbContext>();
        var writer = services.GetRequiredService<FeedPostWriter>();

        var draft = await writer.WriteAsync(bot, now, roll, ct);
        if (draft is null)
        {
            logger.LogInformation("Bot {BotName} had nothing to post", bot.Name);
            return null;
        }

        var post = new Post
        {
            AuthorBotId = bot.Id,
            Content = draft.Content,
            StickerId = draft.Sticker?.Id,
            SourceChatId = draft.SourceChatId,
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bot {BotName} posted on the feed{Source}", bot.Name, draft.SourceChatId is null ? "" : " after a chat");

        if (await FeedReads.PostAsync(db, post.Id, null, ct) is { } dto)
            await services.GetRequiredService<IFeedNotifier>().PostNew(dto);
        if (services.GetService<IFeedQueue>() is { } queue)
            await queue.EnqueueAsync(new FeedEvent(post.Id, null, Depth: 1), ct);
        return post;
    }
}
