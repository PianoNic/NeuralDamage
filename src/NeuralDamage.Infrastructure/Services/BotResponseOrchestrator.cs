using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
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

            // Load the trigger message. ReplyTo is what lets a reply to a bot
            // reach that bot - without it the reply-to rule never fires.
            var message = await db.Messages
                .Include(m => m.SenderUser)
                .Include(m => m.SenderBot)
                .Include(m => m.ReplyTo)
                .FirstOrDefaultAsync(m => m.Id == messageId, cts.Token);

            if (message is null) return;

            // A person spoke since this bot message was queued: the bots answer
            // them instead of carrying on among themselves.
            if (depth > 0 && await db.Messages.AnyAsync(m => m.ChatId == chatId && m.SenderUserId != null && m.CreatedAt > message.CreatedAt, cts.Token))
                return;

            // Get active bots in this chat
            var botMembers = await db.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.BotId != null)
                .Include(cm => cm.Bot)
                .Where(cm => cm.Bot!.IsActive)
                .ToListAsync(cts.Token);

            var bots = botMembers.Select(cm => cm.Bot!).ToList();
            if (bots.Count == 0) return;

            // Decide which bots respond
            var responderIds = await decisionEngine.DecideRespondersAsync(chatId, message, bots, cts.Token);

            var responders = bots.Where(b => responderIds.Contains(b.Id)).ToList();
            if (responders.Count > 0)
                responders = await DropBrokenModelsAsync(scope.ServiceProvider, openRouter, notifications, chatId, responders, cts.Token);
            // A bot never reacts to its own message, and a muted or stopped bot
            // is told to keep quiet, which rules out reacting too.
            var silent = bots.Where(b => !responderIds.Contains(b.Id) && b.Id != message.SenderBotId
                && !botState.IsMuted(chatId, b.Id) && !botState.IsStopped(chatId)).ToList();

            // Reactions run alongside the replies rather than before them: they
            // wait a moment of their own, and the bots that are actually
            // answering should not wait on that.
            reactions = ReactAsync(chatId, message, silent, cts.Token);

            if (responders.Count == 0) return;

            // Get participant names for system prompt
            var members = await db.ChatMembers
                .Where(cm => cm.ChatId == chatId)
                .Include(cm => cm.User)
                .Include(cm => cm.Bot)
                .ToListAsync(cts.Token);
            var participantNames = members.Select(m => m.User?.DisplayName ?? m.Bot?.Name ?? "Unknown").ToList();
            var chatName = await db.Chats.Where(c => c.Id == chatId).Select(c => c.Name).FirstOrDefaultAsync(cts.Token);

            var roundSent = new List<Message>();
            foreach (var bot in responders)
            {
                cts.Token.ThrowIfCancellationRequested();

                // Read the message before starting to type. This also staggers
                // the bots, so they do not all start at once.
                await Task.Delay(BotBehaviorOptions.Between(_options.ReadDelayMin, _options.ReadDelayMax), cts.Token);

                typingShown = true;
                roundSent.AddRange(await RespondAsync(db, openRouter, notifications, chatId, message, bot, participantNames, chatName, cts.Token));
            }

            // One hop per round, however many bots answered: chaining from each
            // responder doubled the rounds at every hop, so two chatty bots
            // could post a couple of dozen messages after a single question.
            await ChainAsync(scope.ServiceProvider, chatId, bots, roundSent, depth + 1, cts.Token);
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
    /// Offers a round's replies to the bots, so they can answer them - until
    /// the chain is <see cref="BotBehaviorOptions.MaxBotChainDepth"/> hops long.
    /// </summary>
    private async Task ChainAsync(IServiceProvider services, Guid chatId, List<Bot> bots, List<Message> sent, int depth, CancellationToken ct)
    {
        if (sent.Count == 0 || depth > _options.MaxBotChainDepth)
            return;

        var queue = services.GetService<IBotResponseQueue>();
        if (queue is null)
            return;

        // The round is decided on once: by the message that names another bot
        // if there is one, since naming is what makes a bot answer.
        var chainFrom = sent.FirstOrDefault(m => bots.Any(b =>
                b.Id != m.SenderBotId && FuzzyNameMatcher.IsNameMentioned(m.Content, b.Name, b.Aliases)))
            ?? sent[^1];

        await queue.EnqueueAsync(chatId, chainFrom.Id, depth, ct);
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
        Guid chatId,
        Message message,
        Bot bot,
        List<string> participantNames,
        string? chatName,
        CancellationToken ct)
    {
        using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var typing = KeepTypingAsync(notifications, chatId, bot, typingCts.Token);
        var sinceTypingStarted = Stopwatch.StartNew();

        try
        {
            // Build history (reload to include any new bot messages from this round)
            var recentMessages = await db.Messages
                .Where(m => m.ChatId == chatId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(50)
                .Include(m => m.SenderUser)
                .Include(m => m.SenderBot)
                .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderUser)
                .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderBot)
                .ToListAsync(ct);
            recentMessages.Reverse();

            var systemPrompt = BotPromptBuilder.BuildSystemPrompt(bot, participantNames, chatName);
            var history = BotPromptBuilder.BuildHistory(recentMessages, bot.Id, message.Id);

            var responseText = await GenerateAsync(openRouter, bot, systemPrompt, history, ct);
            if (string.IsNullOrWhiteSpace(responseText))
            {
                // Both attempts failed or came back empty: say so rather than
                // leave the room waiting on a bot that is never going to answer.
                await notifications.NotifySystemMessage(chatId, $"{bot.Name} failed to respond.");
                return [];
            }

            // Saying the same thing twice is the quickest way to sound like a
            // bot, so a near-repeat of a recent message gets one more try.
            var ownRecent = recentMessages.Where(m => m.SenderBotId == bot.Id).TakeLast(5).Select(m => m.Content).ToList();
            if (BotReplyFormatter.IsNearDuplicate(responseText, ownRecent))
            {
                logger.LogInformation("Bot {BotName} repeated itself; regenerating once", bot.Name);
                var retry = await GenerateAsync(openRouter, bot,
                    systemPrompt + $"\n\nYou were about to say \"{responseText}\", which repeats something you already said. Say something different.",
                    history, ct);
                if (!string.IsNullOrWhiteSpace(retry))
                    responseText = retry;
            }

            // Strip any name prefix the model might add, and any lines it wrote
            // for other people.
            var ownTurn = BotReplyFormatter.DropOtherSpeakers(StripNamePrefix(responseText, bot.Name));
            var parts = BotReplyFormatter.Split(ownTurn, _options.MaxReplyParts);
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
    /// Gives the bots that decided not to reply a chance to react with an
    /// emoji instead, so a quiet bot is not an invisible one.
    /// </summary>
    /// <remarks>
    /// Runs in its own scope because it overlaps the replies, and never throws:
    /// a reaction that fails to land is not worth failing a round over.
    /// </remarks>
    private async Task ReactAsync(Guid chatId, Message message, List<Bot> silent, CancellationToken ct)
    {
        // No keyword, no reaction: a random emoji on a sad message is worse than none.
        var emoji = BotReactionService.SelectEmoji(message.Content);
        if (emoji is null)
            return;

        var reactors = silent.Where(_ => BotReactionService.ShouldReact()).ToList();
        if (reactors.Count == 0)
            return;

        try
        {
            await Task.Delay(BotBehaviorOptions.Between(_options.ReactionDelayMin, _options.ReactionDelayMax), ct);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<IChatNotificationService>();
            var reacted = false;

            foreach (var bot in reactors)
            {
                var already = await db.Reactions.AnyAsync(
                    r => r.MessageId == message.Id && r.BotId == bot.Id && r.Emoji == emoji, ct);
                if (already)
                    continue;

                db.Reactions.Add(new Reaction { MessageId = message.Id, BotId = bot.Id, Emoji = emoji });
                reacted = true;

                logger.LogInformation("Bot {BotName} reacted with {Emoji}", bot.Name, emoji);
            }

            if (!reacted)
                return;

            await db.SaveChangesAsync(ct);

            // One broadcast for the round: the client replaces the whole reaction
            // set for the message, so sending it per bot would just be redundant.
            var reactions = await db.Reactions
                .Where(r => r.MessageId == message.Id)
                .Include(r => r.User)
                .Include(r => r.Bot)
                .AsNoTracking()
                .ToListAsync(ct);

            await notifications.NotifyReactionUpdated(chatId, message.Id, reactions.ToGroups());
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to add bot reactions in chat {ChatId}", chatId);
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

    private static string StripNamePrefix(string text, string botName)
    {
        // Strip patterns like "[BotName]: " or "BotName: "
        var prefixes = new[] { $"[{botName}]: ", $"[{botName}]:", $"{botName}: " };
        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return text[prefix.Length..].TrimStart();
        }
        return text;
    }
}
