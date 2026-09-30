using Microsoft.Extensions.Logging;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

/// <summary>
/// Asks Jev (OpenRouter Decisions API) one yes/no question per undecided bot:
/// would this persona naturally reply here? The policy - threshold and how
/// many may reply - stays in code.
/// </summary>
public class Tier3LlmJudge(IDecisionsClient decisions, BotRankingOptions options, ILogger<Tier3LlmJudge> logger)
{
    private const int HistoryMessages = 10;
    private const int MaxMessageChars = 400;
    private const int MaxSystemPromptChars = 500;

    private const string RespondCriterion =
        "A real person with this persona would naturally chime in now: `new_message` is addressed to them by name "
        + "or to the whole room, asks something this persona would have an opinion or knowledge about, or continues "
        + "a thread in `recent_messages` that this bot was already part of. When several bots in `bots` fit, only "
        + "the one or two best fits by persona should reply.";

    private const string SilentCriterion =
        "Replying would be noise: `new_message` is chatter between other people, is aimed at a different bot, this "
        + "bot just spoke and has nothing new to add, the topic is outside this persona, or another bot in `bots` "
        + "is a clearly better fit. In a group chat most people stay quiet on most messages.";

    public async Task<List<Guid>> JudgeAsync(Message message, List<(Bot Bot, double Tier2Score)> undecidedBots, List<Message> recentHistory, CancellationToken ct)
    {
        try
        {
            var keyed = undecidedBots.Select((b, i) => (Key: $"bot_{i}", b.Bot)).ToList();

            var state = new
            {
                RecentMessages = recentHistory
                    .Where(m => m.Id != message.Id)
                    .TakeLast(HistoryMessages)
                    .Select(m => new { Sender = SenderName(m), IsBot = m.SenderBotId is not null, Text = Trim(m.Content, MaxMessageChars) })
                    .ToList(),
                NewMessage = new
                {
                    Sender = SenderName(recentHistory.FirstOrDefault(m => m.Id == message.Id) ?? message),
                    IsBot = message.SenderBotId is not null,
                    Text = Trim(message.Content, MaxMessageChars),
                },
                Bots = keyed.ToDictionary(k => k.Key, k => new { k.Bot.Name, Persona = Persona(k.Bot) }),
            };

            var questions = keyed.ToDictionary(
                k => k.Key,
                k => new DecisionQuestion(
                    "noul",
                    $"Would the bot `bots.{k.Key}` (named in `bots.{k.Key}.name`, persona in `bots.{k.Key}.persona`) "
                    + "naturally reply to `new_message` in this group chat, given `recent_messages`? "
                    + "Usually only one or two bots should reply to a message, never all of them.",
                    new NoulCriteria(RespondCriterion, SilentCriterion)));

            var response = await decisions.DecideAsync(state, questions, ct);
            if (response?.Answers is null)
                return FallbackToTier2(undecidedBots);

            var scored = keyed
                .Select(k => (k.Bot, P: response.Answers.TryGetValue(k.Key, out var a) ? a.Noul ?? 0 : 0))
                .ToList();

            logger.LogInformation("Jev probabilities {Probabilities} (threshold {Threshold}, max {Max}), cost {Cost}",
                string.Join(", ", scored.Select(s => $"{s.Bot.Name}={s.P:F2}")),
                options.Threshold, options.MaxResponders, response.Usage?.Cost);

            return scored
                .Where(s => s.P >= options.Threshold)
                .OrderByDescending(s => s.P)
                .Take(options.MaxResponders)
                .Select(s => s.Bot.Id)
                .ToList();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Tier 3 judge failed; falling back to Tier 2 scores.");
            return FallbackToTier2(undecidedBots);
        }
    }

    private static string SenderName(Message m) =>
        m.SenderUser?.DisplayName is { Length: > 0 } user ? user : m.SenderBot?.Name ?? "Unknown";

    private static string Persona(Bot bot) =>
        string.Join(" ", new[] { bot.Personality, Trim(bot.SystemPrompt, MaxSystemPromptChars) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string Trim(string? text, int max) =>
        text is null ? string.Empty : text.Length > max ? text[..max] + "..." : text;

    private static List<Guid> FallbackToTier2(List<(Bot Bot, double Tier2Score)> candidates)
        => candidates.Where(b => b.Tier2Score > 0.4).Select(b => b.Bot.Id).ToList();
}
