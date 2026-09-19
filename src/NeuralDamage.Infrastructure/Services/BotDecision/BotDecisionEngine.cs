using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

public class BotDecisionEngine(NeuralDamageDbContext db, Tier3LlmJudge tier3Judge, ILogger<BotDecisionEngine> logger) : IBotDecisionEngine
{
    public async Task<List<Guid>> DecideRespondersAsync(Guid chatId, Message message, List<Bot> candidateBots, CancellationToken ct = default)
    {
        var mustRespond = new List<Guid>();
        var undecided = new List<(Bot Bot, double Score)>();

        // Load context for Tier 2
        var recentMessages = await db.Messages
            .Where(m => m.ChatId == chatId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(20)
            .AsNoTracking()
            .ToListAsync(ct);

        var totalBotsInChat = candidateBots.Count;

        foreach (var bot in candidateBots)
        {
            // Tier 1: Hard rules
            var tier1 = Tier1HardRules.Evaluate(message, bot, isMuted: false, isStopped: false);
            if (tier1 != Tier1Result.Undecided)
                logger.LogInformation("Bot {Bot}: tier 1 says {Tier1}", bot.Name, tier1);
            if (tier1 == Tier1Result.MustRespond) { mustRespond.Add(bot.Id); continue; }
            if (tier1 == Tier1Result.MustSkip) continue;

            // Tier 2: Weighted score
            var botMessagesInLast20 = recentMessages.Count(m => m.SenderBotId == bot.Id);
            var lastBotMessage = recentMessages.FirstOrDefault(m => m.SenderBotId == bot.Id);
            var secondsSince = lastBotMessage is not null
                ? (int)(DateTime.UtcNow - lastBotMessage.CreatedAt).TotalSeconds
                : -1; // never spoke

            var context = new Tier2Context(
                IsGroupQuestion: message.Content.Contains('?'),
                BotMessagesInLast20: botMessagesInLast20,
                TotalRecentMessages: recentMessages.Count,
                SecondsSinceLastBotMessage: secondsSince,
                MessageLength: message.Content.Length,
                TotalBotsInChat: totalBotsInChat);

            var score = Tier2WeightedScore.ComputeScore(context);
            logger.LogInformation(
                "Bot {Bot}: tier 2 score {Score:F2} (respond >= {Respond}, skip < {Skip})",
                bot.Name, score, Tier2WeightedScore.RespondThreshold, Tier2WeightedScore.SkipThreshold);

            if (score >= Tier2WeightedScore.RespondThreshold) { mustRespond.Add(bot.Id); continue; }
            if (score < Tier2WeightedScore.SkipThreshold) continue;

            undecided.Add((bot, score));
        }

        // Tier 3 exists to choose between bots. With a single bot in the chat
        // there is nothing to disambiguate, so asking a model "which of these
        // one bots should reply?" only adds a round trip and a chance of an
        // unexplained silence. Tier 2 has already had its say via SkipThreshold.
        if (undecided.Count > 0 && totalBotsInChat == 1)
        {
            logger.LogInformation("Single bot in chat; responding without a tier 3 call");
            mustRespond.AddRange(undecided.Select(u => u.Bot.Id));
            undecided.Clear();
        }

        // Tier 3: Single LLM call for all undecided bots
        if (undecided.Count > 0)
        {
            var history = recentMessages
                .OrderBy(m => m.CreatedAt)
                .Select(m => new ChatMessage(
                    m.SenderBotId is not null ? "assistant" : "user",
                    m.Content))
                .ToList();

            var judged = await tier3Judge.JudgeAsync(message, undecided, history, ct);
            logger.LogInformation("Tier 3 judged {Judged} of {Undecided} undecided bots as responders",
                judged.Count, undecided.Count);
            mustRespond.AddRange(judged);
        }

        logger.LogInformation("Decision for chat {ChatId}: {Count} responder(s)", chatId, mustRespond.Count);
        return mustRespond;
    }
}
