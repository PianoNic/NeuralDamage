using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services;

public class BotResponseOrchestrator(
    IServiceScopeFactory scopeFactory,
    IChatBotState botState,
    ILogger<BotResponseOrchestrator> logger,
    IOptions<BotBehaviorOptions>? options = null) : IBotResponseOrchestrator
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeTasks = new();
    private readonly BotBehaviorOptions _options = options?.Value ?? new();

    public async Task ProcessMessageAsync(Guid chatId, Guid messageId, int depth = 0, CancellationToken ct = default)
    {
        CancelPendingResponses(chatId);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeTasks[chatId] = cts;
        var reactions = Task.CompletedTask;
        var typingShown = false;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            var decisionEngine = scope.ServiceProvider.GetRequiredService<IBotDecisionEngine>();
            var openRouter = scope.ServiceProvider.GetRequiredService<IOpenRouterService>();
            var notifications = scope.ServiceProvider.GetRequiredService<IChatNotificationService>();

            // Text-only bots and Jev read a picture through its description,
            // which was started on upload and is usually done by now.
            await WaitForDescriptionsAsync(scope.ServiceProvider, db, messageId, cts.Token);

            // ReplyTo is what Jev reads as "this answers Rex", and what the
            // fallback uses to pick the bot replied to.
            var message = await db.Messages
                .Include(m => m.SenderUser)
                .Include(m => m.SenderBot)
                .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderUser)
                .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderBot)
                .Include(m => m.Attachments)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == messageId, cts.Token);

            if (message is null || message.Content.StartsWith('/')) return;

            // A person spoke since this bot message was queued: the bots answer
            // them instead of carrying on among themselves.
            if (depth > 0 && await db.Messages.AnyAsync(m => m.ChatId == chatId && m.SenderUserId != null && m.CreatedAt > message.CreatedAt, cts.Token))
                return;

            // The only things applied before Jev are the user's own commands -
            // /mute and /stop - and that a bot never answers itself.
            var bots = await db.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.BotId != null && cm.Bot!.IsActive)
                .Select(cm => cm.Bot!)
                .AsNoTracking()
                .ToListAsync(cts.Token);
            bots = bots.Where(b => b.Id != message.SenderBotId && !botState.IsMuted(chatId, b.Id) && !botState.IsStopped(chatId)).ToList();
            if (bots.Count == 0) return;

            // Everything the replies read is as it stands now: a bot answers
            // this message, not what the others are about to say about it.
            var roundStart = message.CreatedAt > DateTime.UtcNow ? message.CreatedAt : DateTime.UtcNow;

            var decided = await decisionEngine.DecideAsync(chatId, message, bots, cts.Token);

            // Reactions land on their own timing, alongside the replies.
            reactions = Task.WhenAll(decided
                .Where(d => d.Action == BotAction.React && d.Emoji is not null)
                .Select(d => ReactAsync(chatId, message.Id, d.Bot, d.Emoji!, cts.Token)));

            var responders = decided.Where(d => d.Action == BotAction.Reply).Select(d => d.Bot).ToList();
            if (responders.Count > 0)
                responders = await DropBrokenModelsAsync(scope.ServiceProvider, openRouter, notifications, chatId, responders, cts.Token);
            if (responders.Count == 0) return;

            // Sorted: the database gives no order, and the names are part of
            // the system prompt, which has to read the same on every call.
            var members = await db.ChatMembers
                .Where(cm => cm.ChatId == chatId)
                .Include(cm => cm.User)
                .Include(cm => cm.Bot)
                .AsNoTracking()
                .ToListAsync(cts.Token);
            var participantNames = members.Select(m => m.User?.DisplayName ?? m.Bot?.Name ?? "Unknown").Order(StringComparer.Ordinal).ToList();
            var chatName = await db.Chats.Where(c => c.Id == chatId).Select(c => c.Name).FirstOrDefaultAsync(cts.Token);
            var canSee = await VisionModelsAsync(openRouter, cts.Token);
            var round = new Round(chatId, message, roundStart, participantNames, chatName);

            // Every replying bot starts at once, each with its own read delay,
            // typing, generation and post. Each works in a scope of its own:
            // an EF context is not safe to share between them.
            typingShown = true;
            var replies = await Task.WhenAll(responders.Select(bot => ReplyAsync(round, bot, canSee.Contains(bot.ModelId), cts.Token)));

            // Each reply is a new message and gets its own single Jev call,
            // which is how bots answer each other - up to the hop limit.
            var chained = replies.Where(r => r.Count > 0).Select(r => r[^1]).OrderBy(m => m.CreatedAt).ToList();
            await ChainAsync(scope.ServiceProvider, chatId, chained, depth + 1, cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Bot response processing cancelled for chat {ChatId}", chatId);
            if (typingShown)
                await NotifyCancelledAsync(chatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing bot responses for chat {ChatId}", chatId);
        }
        finally
        {
            // Reactions share the round's token, so it must outlive them.
            await reactions;

            // Only remove our own entry: a newer round may already have replaced it.
            _activeTasks.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(chatId, cts));
            cts.Dispose();
        }
    }

    /// <summary>What every bot replying to one message shares.</summary>
    private sealed record Round(Guid ChatId, Message Message, DateTime Start, List<string> ParticipantNames, string? ChatName);

    /// <summary>
    /// One bot's reply, in its own scope: read, type, generate, post. A failure
    /// is this bot's alone; only cancellation ends the others too.
    /// </summary>
    private async Task<List<Message>> ReplyAsync(Round round, Bot bot, bool canSee, CancellationToken ct)
    {
        try
        {
            await Task.Delay(BotBehaviorOptions.Between(_options.ReadDelayMin, _options.ReadDelayMax), ct);

            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            return await RespondAsync(
                services.GetRequiredService<NeuralDamageDbContext>(),
                services.GetRequiredService<IOpenRouterService>(),
                services.GetRequiredService<IChatNotificationService>(),
                round, bot, canSee ? services.GetService<IAttachmentStorage>() : null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Bot {BotName} failed to reply in chat {ChatId}", bot.Name, round.ChatId);
            return [];
        }
    }

    /// <summary>
    /// Leaves out responders whose model is gone from OpenRouter or refused by
    /// the policy: asking would only fail. The chat is told once per bot, not
    /// on every message, until the bot's model is changed.
    /// </summary>
    private async Task<List<Bot>> DropBrokenModelsAsync(IServiceProvider services, IOpenRouterService openRouter, IChatNotificationService notifications, Guid chatId, List<Bot> responders, CancellationToken ct)
    {
        var lookup = await services.GetRequiredService<ModelPolicy>().StatusLookupAsync(openRouter, ct);
        var working = new List<Bot>();
        foreach (var bot in responders)
        {
            var status = lookup(bot.ModelId);
            if (status.IsAvailable)
            {
                working.Add(bot);
                continue;
            }

            if (!botState.TryMarkModelNotice(chatId, bot.Id))
                continue;
            logger.LogInformation("Bot {BotName} skipped in chat {ChatId}: {Reason}", bot.Name, chatId, status.Reason);
            await notifications.NotifySystemMessage(chatId, $"{bot.Name}'s model {bot.ModelId} is no longer available. Edit the bot to pick another.");
        }
        return working;
    }

    /// <summary>
    /// Queues each reply of the round as a message of its own, so every one
    /// gets its own Jev call - until the chain is
    /// <see cref="BotBehaviorOptions.MaxBotChainDepth"/> hops long. A reply
    /// split into parts is queued by its last part; Jev reads the rest as the
    /// messages before it.
    /// </summary>
    private async Task ChainAsync(IServiceProvider services, Guid chatId, List<Message> replies, int depth, CancellationToken ct)
    {
        if (replies.Count == 0 || depth > _options.MaxBotChainDepth)
            return;

        var queue = services.GetService<IBotResponseQueue>();
        if (queue is null)
            return;

        foreach (var reply in replies)
            await queue.EnqueueAsync(chatId, reply.Id, depth, ct);
    }

    /// <summary>
    /// One bot's turn: generate while showing it typing, then post the reply -
    /// split into a few messages if it has separate thoughts - paced like
    /// someone typing it. Returns the messages it posted.
    /// </summary>
    private async Task<List<Message>> RespondAsync(
        NeuralDamageDbContext db,
        IOpenRouterService openRouter,
        IChatNotificationService notifications,
        Round round,
        Bot bot,
        IAttachmentStorage? images,
        CancellationToken ct)
    {
        var (chatId, message) = (round.ChatId, round.Message);
        using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var typing = KeepTypingAsync(notifications, chatId, bot, typingCts.Token);
        var sinceTypingStarted = Stopwatch.StartNew();

        try
        {
            // The history as it stood when the round started: the bots reply
            // side by side, not to each other. It starts where this bot's last
            // history started, so the prompt begins the same way and hits the
            // provider's cache; the window only moves on once it outgrows the
            // budget.
            var query = db.Messages.Where(m => m.ChatId == chatId && m.CreatedAt <= round.Start);
            if (botState.HistoryStart(chatId, bot.Id) is { } historyStart)
                query = query.Where(m => m.CreatedAt >= historyStart);
            var recentMessages = await query
                .OrderByDescending(m => m.CreatedAt)
                .Take(BotPromptBuilder.MaxHistoryMessages * 2)
                .Include(m => m.SenderUser)
                .Include(m => m.SenderBot)
                .Include(m => m.Attachments)
                .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderUser)
                .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderBot)
                .ToListAsync(ct);
            recentMessages.Reverse();

            var window = BotPromptBuilder.TrimHistory(recentMessages, message.Id);
            if (window.Count > 0)
                botState.SetHistoryStart(chatId, bot.Id, window[0].CreatedAt);

            var systemPrompt = BotPromptBuilder.BuildSystemPrompt(bot, round.ParticipantNames, round.ChatName);
            var pictures = images is null ? null : await LoadImagesAsync(images, window, ct);
            var history = BotPromptBuilder.BuildHistory(window, bot.Id, message.Id, pictures);

            var responseText = await GenerateAsync(openRouter, bot, systemPrompt, [.. history, BotPromptBuilder.BuildNote()], ct);
            if (string.IsNullOrWhiteSpace(responseText))
            {
                // Both attempts failed or came back empty: say so rather than
                // leave the room waiting on a bot that is never going to answer.
                await notifications.NotifySystemMessage(chatId, $"{bot.Name} failed to respond.");
                return [];
            }

            // Saying the same thing twice is the quickest way to sound like a
            // bot, so a near-repeat of a recent message gets one more try, and
            // is dropped if the model repeats itself again. The check runs on
            // what would actually be posted, and against whole earlier replies
            // as well as their parts: a reply split into a few messages is
            // never a near-duplicate of any one of them on its own.
            var ownRecent = OwnRecentReplies(recentMessages, bot.Id);
            var parts = ToParts(responseText, bot.Name);
            if (parts.Count == 0)
            {
                // Only a stage direction or punctuation: nothing worth posting.
                logger.LogInformation("Bot {BotName} replied with filler only; dropping it", bot.Name);
                return [];
            }
            if (Repeats(parts, ownRecent))
            {
                logger.LogInformation("Bot {BotName} repeated itself; regenerating once", bot.Name);
                var retry = await GenerateAsync(openRouter, bot, systemPrompt,
                    [.. history, BotPromptBuilder.BuildNote(instruction: $"You were about to say \"{responseText}\", which repeats something you already said. Say something different.")],
                    ct);
                parts = string.IsNullOrWhiteSpace(retry) ? [] : ToParts(retry, bot.Name);
                if (parts.Count == 0 || Repeats(parts, ownRecent))
                {
                    logger.LogInformation("Bot {BotName} repeated itself again; dropping the reply", bot.Name);
                    return [];
                }
            }

            var sent = new List<Message>();

            for (var i = 0; i < parts.Count; i++)
            {
                // Keep typing for about as long as a person would need for this
                // part. Generating the first one already took a while, and that
                // counts: a slow model does not then also type slowly.
                var remaining = _options.TypingTime(parts[i].Length) - (i == 0 ? sinceTypingStarted.Elapsed : TimeSpan.Zero);
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, ct);

                // Stop the indicator before the last part lands, so a late
                // heartbeat cannot bring it back after the message.
                if (i == parts.Count - 1)
                {
                    await typingCts.CancelAsync();
                    await typing;
                }

                // Only the first part carries the reply link; the rest read as
                // the same person carrying on.
                var botMessage = new Message
                {
                    ChatId = chatId,
                    SenderBotId = bot.Id,
                    Content = parts[i],
                    ReplyToId = i == 0 ? message.Id : null
                };
                db.Messages.Add(botMessage);
                await db.SaveChangesAsync(ct);
                sent.Add(botMessage);

                // Broadcast
                var loaded = await db.Messages
                    .Include(m => m.SenderBot)
                    .Include(m => m.Reactions).ThenInclude(r => r.User)
                    .Include(m => m.Reactions).ThenInclude(r => r.Bot)
                    .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderUser)
                    .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderBot)
                    .AsNoTracking()
                    .FirstAsync(m => m.Id == botMessage.Id, ct);
                await notifications.NotifyMessageNew(chatId, loaded.ToDto());
            }

            return sent;
        }
        finally
        {
            await typingCts.CancelAsync();
            await typing;
        }
    }

    /// <summary>
    /// Waits, for at most <see cref="AttachmentOptions.DescriptionWait"/>, for
    /// the describer to finish the trigger message's images. Past that the
    /// round goes on and text-only bots see a bare "[image from alice]".
    /// </summary>
    private static async Task WaitForDescriptionsAsync(IServiceProvider services, NeuralDamageDbContext db, Guid messageId, CancellationToken ct)
    {
        if (services.GetService<IImageDescriber>() is not { } describer)
            return;
        var pending = await db.Attachments
            .Where(a => a.MessageId == messageId && a.Description == null)
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return;
        var wait = (services.GetService<AttachmentOptions>() ?? new AttachmentOptions()).DescriptionWait;
        await describer.WaitAsync(pending, wait, ct);
    }

    /// <summary>
    /// The ids of the models that can see images, from the catalogue. When it
    /// cannot be read every bot is treated as text-only, which always works.
    /// </summary>
    private async Task<IReadOnlySet<string>> VisionModelsAsync(IOpenRouterService openRouter, CancellationToken ct)
    {
        try
        {
            var models = await openRouter.ListModelsAsync(ct);
            return (models ?? []).Where(m => m.Capabilities.Contains(ModelMetadata.Vision)).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Model catalogue unavailable; every bot gets image descriptions instead of images");
            return new HashSet<string>();
        }
    }

    /// <summary>
    /// How many of the history's most recent images a vision model gets as
    /// pictures. Each one is resent on every turn, so older ones go as their
    /// descriptions instead.
    /// </summary>
    public const int MaxHistoryImages = 4;

    private static async Task<Dictionary<Guid, ImagePart>> LoadImagesAsync(IAttachmentStorage storage, List<Message> window, CancellationToken ct)
    {
        var recent = window
            .SelectMany(m => m.Attachments)
            .OrderByDescending(a => a.CreatedAt)
            .Take(MaxHistoryImages);
        var images = new Dictionary<Guid, ImagePart>();
        foreach (var attachment in recent)
            if (await storage.ReadAllAsync(attachment.ChatId, attachment.Id, ct) is { } data)
                images[attachment.Id] = new ImagePart(attachment.ContentType, data);
        return images;
    }

    /// <summary>
    /// Generates a reply, or returns empty when the model gives nothing usable.
    /// </summary>
    private async Task<string> GenerateAsync(IOpenRouterService openRouter, Bot bot, string systemPrompt, List<ChatMessage> history, CancellationToken ct)
    {
        // Sampling is non-deterministic and a model run hot will occasionally
        // return nothing at all, so one empty result is worth a second attempt
        // before giving up - otherwise the bot just looks broken to the person
        // who asked.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            // A model run hot can spiral: at temperature 2 one reasoning
            // model spent 2494 tokens thinking and returned nothing, or
            // returned garbage. Retrying at the same setting mostly
            // reproduces it, so the second attempt backs the temperature
            // off to a range these models stay coherent in.
            var attemptTemperature = attempt == 1
                ? bot.Temperature
                : Math.Min(bot.Temperature, 1.0);

            string responseText;
            try
            {
                responseText = await openRouter.GenerateResponseAsync(bot.ModelId, attemptTemperature, systemPrompt, history, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to generate response for bot {BotName}", bot.Name);
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(responseText))
                return responseText;

            logger.LogWarning(
                "Bot {BotName} returned an empty response for model {ModelId} at temperature {Temperature} (attempt {Attempt} of 2)",
                bot.Name, bot.ModelId, attemptTemperature, attempt);
        }

        return string.Empty;
    }

    /// <summary>
    /// Shows the bot typing until <paramref name="ct"/> is cancelled. The event
    /// is re-sent on a heartbeat, because the client expires an indicator a few
    /// seconds after the last one - a fixed timer lost it on slow models.
    /// </summary>
    private async Task KeepTypingAsync(IChatNotificationService notifications, Guid chatId, Bot bot, CancellationToken ct)
    {
        try
        {
            do
            {
                await notifications.NotifyBotTyping(chatId, bot.Id, bot.Name);
                if (_options.TypingHeartbeat <= TimeSpan.Zero)
                    return;
                await Task.Delay(_options.TypingHeartbeat, ct);
            }
            while (!ct.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send typing for bot {BotName}", bot.Name);
        }
    }

    /// <summary>Clears the typing indicators of a round that was cut short.</summary>
    private async Task NotifyCancelledAsync(Guid chatId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IChatNotificationService>().NotifyBotResponseCancelled(chatId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send response cancelled for chat {ChatId}", chatId);
        }
    }

    /// <summary>
    /// One bot's reaction, after a moment of its own. Runs in its own scope
    /// because it overlaps the replies, and never throws: a reaction that fails
    /// to land is not worth failing a round over.
    /// </summary>
    private async Task ReactAsync(Guid chatId, Guid messageId, Bot bot, string emoji, CancellationToken ct)
    {
        try
        {
            await Task.Delay(BotBehaviorOptions.Between(_options.ReactionDelayMin, _options.ReactionDelayMax), ct);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<IChatNotificationService>();

            if (await db.Reactions.AnyAsync(r => r.MessageId == messageId && r.BotId == bot.Id && r.Emoji == emoji, ct))
                return;

            db.Reactions.Add(new Reaction { MessageId = messageId, BotId = bot.Id, Emoji = emoji });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Bot {BotName} reacted with {Emoji}", bot.Name, emoji);

            // The client replaces the message's whole reaction set.
            var reactions = await db.Reactions
                .Where(r => r.MessageId == messageId)
                .Include(r => r.User)
                .Include(r => r.Bot)
                .AsNoTracking()
                .ToListAsync(ct);

            await notifications.NotifyReactionUpdated(chatId, messageId, reactions.ToGroups());
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to add {BotName}'s reaction in chat {ChatId}", bot.Name, chatId);
        }
    }

    public void CancelPendingResponses(Guid chatId)
    {
        // Cancel only. The round that owns the source is still using its token
        // and disposes it in its own finally; disposing here made an ordinary
        // cancel surface as an ObjectDisposedException.
        if (!_activeTasks.TryRemove(chatId, out var cts))
            return;

        try { cts.Cancel(); }
        catch (ObjectDisposedException) { } // the round finished in between; nothing to cancel
    }

    /// <summary>
    /// Cleans a model's reply into the messages that would be posted: its own
    /// name prefix and any lines written for other people go, then it is split,
    /// and parts that are only a stage direction or punctuation are dropped.
    /// </summary>
    private List<string> ToParts(string reply, string botName) =>
        BotReplyFormatter.Split(BotReplyFormatter.DropOtherSpeakers(BotReplyFormatter.StripOwnName(reply, botName)), _options.MaxReplyParts)
            .Where(p => !BotReplyFormatter.IsFiller(p))
            .ToList();

    private static bool Repeats(List<string> parts, List<string> ownRecent) =>
        BotReplyFormatter.IsNearDuplicate(string.Join("\n", parts), ownRecent)
        || parts.Any(p => BotReplyFormatter.IsNearDuplicate(p, ownRecent));

    /// <summary>
    /// The bot's last few messages, plus its last few replies put back
    /// together: a reply starts with the message that carries the reply link,
    /// and the parts after it carry on without one.
    /// </summary>
    private static List<string> OwnRecentReplies(IEnumerable<Message> chronological, Guid botId)
    {
        var own = chronological.Where(m => m.SenderBotId == botId).ToList();
        var replies = new List<List<string>>();
        foreach (var m in own)
        {
            if (m.ReplyToId is not null || replies.Count == 0)
                replies.Add([]);
            replies[^1].Add(m.Content);
        }

        return own.TakeLast(5).Select(m => m.Content)
            .Concat(replies.TakeLast(3).Where(r => r.Count > 1).Select(r => string.Join("\n", r)))
            .ToList();
    }
}
