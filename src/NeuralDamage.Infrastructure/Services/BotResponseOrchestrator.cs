using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services;

public class BotResponseOrchestrator(IServiceScopeFactory scopeFactory, ILogger<BotResponseOrchestrator> logger) : IBotResponseOrchestrator
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeTasks = new();

    public async Task ProcessMessageAsync(Guid chatId, Guid messageId, CancellationToken ct = default)
    {
        CancelPendingResponses(chatId);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeTasks[chatId] = cts;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            var decisionEngine = scope.ServiceProvider.GetRequiredService<IBotDecisionEngine>();
            var openRouter = scope.ServiceProvider.GetRequiredService<IOpenRouterService>();
            var notifications = scope.ServiceProvider.GetRequiredService<IChatNotificationService>();

            // Load the trigger message
            var message = await db.Messages
                .Include(m => m.SenderUser)
                .Include(m => m.SenderBot)
                .FirstOrDefaultAsync(m => m.Id == messageId, cts.Token);

            if (message is null) return;

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
            var silent = bots.Where(b => !responderIds.Contains(b.Id)).ToList();

            // Reactions come before replies. They cost nothing - no model call,
            // just a keyword match - so landing them first means something
            // visible happens the moment a message is sent, while the bots that
            // are actually answering are still generating.
            await ReactAsync(db, notifications, chatId, message, silent, cts.Token);

            if (responders.Count == 0) return;

            // Get participant names for system prompt
            var members = await db.ChatMembers
                .Where(cm => cm.ChatId == chatId)
                .Include(cm => cm.User)
                .Include(cm => cm.Bot)
                .ToListAsync(cts.Token);
            var participantNames = members.Select(m => m.User?.DisplayName ?? m.Bot?.Name ?? "Unknown").ToList();

            // Generate responses with stagger
            foreach (var bot in responders)
            {
                cts.Token.ThrowIfCancellationRequested();

                // Notify typing
                await notifications.NotifyBotTyping(chatId, bot.Id, bot.Name);

                // Build history (reload to include any new bot messages from this round)
                var recentMessages = await db.Messages
                    .Where(m => m.ChatId == chatId)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(50)
                    .Include(m => m.SenderUser)
                    .Include(m => m.SenderBot)
                    .ToListAsync(cts.Token);
                recentMessages.Reverse();

                var systemPrompt = BotPromptBuilder.BuildSystemPrompt(bot, participantNames);
                var history = BotPromptBuilder.BuildHistory(recentMessages, bot.Id);

                // Generate response. Sampling is non-deterministic and a model
                // run hot will occasionally return nothing at all, so one empty
                // result is worth a second attempt before giving up - otherwise
                // the bot just looks broken to the person who asked.
                string responseText = string.Empty;
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
                    try
                    {
                        responseText = await openRouter.GenerateResponseAsync(bot.ModelId, attemptTemperature, systemPrompt, history, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to generate response for bot {BotName}", bot.Name);
                        responseText = string.Empty;
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(responseText))
                        break;

                    logger.LogWarning(
                        "Bot {BotName} returned an empty response for model {ModelId} at temperature {Temperature} (attempt {Attempt} of 2)",
                        bot.Name, bot.ModelId, attemptTemperature, attempt);
                }

                if (string.IsNullOrWhiteSpace(responseText))
                    continue;

                // Strip any name prefix the model might add
                responseText = StripNamePrefix(responseText, bot.Name);

                // Save bot message
                var botMessage = new Message
                {
                    ChatId = chatId,
                    SenderBotId = bot.Id,
                    Content = responseText,
                    ReplyToId = message.Id
                };
                db.Messages.Add(botMessage);
                await db.SaveChangesAsync(cts.Token);

                // Broadcast
                var loaded = await db.Messages
                    .Include(m => m.SenderBot)
                    .Include(m => m.Reactions).ThenInclude(r => r.User)
                    .Include(m => m.Reactions).ThenInclude(r => r.Bot)
                    .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderUser)
                    .Include(m => m.ReplyTo!).ThenInclude(r => r.SenderBot)
                    .AsNoTracking()
                    .FirstAsync(m => m.Id == botMessage.Id, cts.Token);
                await notifications.NotifyMessageNew(chatId, loaded.ToDto());

                // Stagger between bots, but never after the last one - a
                // single-bot chat was paying up to two seconds for nothing.
                if (!ReferenceEquals(bot, responders[^1]))
                    await Task.Delay(Random.Shared.Next(400, 1200), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Bot response processing cancelled for chat {ChatId}", chatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing bot responses for chat {ChatId}", chatId);
        }
        finally
        {
            _activeTasks.TryRemove(chatId, out _);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Gives the bots that decided not to reply a chance to react with an
    /// emoji instead, so a quiet bot is not an invisible one.
    /// </summary>
    /// <remarks>
    /// Only messages a person sent reach this orchestrator, so a bot can never
    /// end up reacting to itself and there is no reply chain to bound - which
    /// is what the legacy implementation needed its depth check for.
    /// </remarks>
    private async Task ReactAsync(
        NeuralDamageDbContext db,
        IChatNotificationService notifications,
        Guid chatId,
        Message message,
        List<Bot> silent,
        CancellationToken ct)
    {
        var reacted = false;

        foreach (var bot in silent)
        {
            ct.ThrowIfCancellationRequested();

            if (!BotReactionService.ShouldReact())
                continue;

            var emoji = BotReactionService.SelectEmoji(message.Content);
            if (string.IsNullOrEmpty(emoji))
                continue;

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

    public void CancelPendingResponses(Guid chatId)
    {
        if (_activeTasks.TryRemove(chatId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
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
