using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NeuralDamage.Application.Dtos;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Mappers;
using NeuralDamage.Application.Services;
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
            var db = scope.ServiceProvider.GetRequiredService<INeuralDamageDbContext>();
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
            if (responderIds.Count == 0) return;

            var responders = bots.Where(b => responderIds.Contains(b.Id)).ToList();

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

                // Generate response
                string responseText;
                try
                {
                    responseText = await openRouter.GenerateResponseAsync(bot.ModelId, bot.Temperature, systemPrompt, history, cts.Token);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to generate response for bot {BotName}", bot.Name);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(responseText)) continue;

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
                    .AsNoTracking()
                    .FirstAsync(m => m.Id == botMessage.Id, cts.Token);
                await notifications.NotifyMessageNew(chatId, loaded.ToDto());

                // Stagger between bots (500ms - 2s)
                var delay = Random.Shared.Next(500, 2000);
                await Task.Delay(delay, cts.Token);
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
