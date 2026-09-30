using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

/// <summary>
/// Jev decides what every bot does with a message, in one call: one
/// <c>choice</c> question per bot, each judged on that bot's persona and the
/// message alone, plus one <c>score</c> question on how the conversation is
/// going, which holds back bots answering bots once they start going in
/// circles. The code only turns the probabilities into actions.
/// </summary>
public class BotDecisionEngine(
    NeuralDamageDbContext db,
    IDecisionsClient decisions,
    BotRankingOptions ranking,
    ILogger<BotDecisionEngine> logger,
    IOptions<BotBehaviorOptions>? options = null) : IBotDecisionEngine
{
    public const string Reply = "reply";
    public const string ReactLaugh = "react_laugh";
    public const string ReactLove = "react_love";
    public const string ReactWow = "react_wow";
    public const string ReactThumbs = "react_thumbs";
    public const string Quiet = "quiet";

    /// <summary>The six options every bot is asked about, and what each means.</summary>
    public static readonly IReadOnlyDictionary<string, string> Criteria = new Dictionary<string, string>
    {
        [Reply] = "They have something to say about it, or it is addressed to them.",
        [ReactLaugh] = "They find it funny but have nothing to add.",
        [ReactLove] = "They like it or agree, without writing anything.",
        [ReactWow] = "It surprises or impresses them.",
        [ReactThumbs] = "A quick acknowledgement is enough.",
        [Quiet] = "It doesn't concern them; they scroll past.",
    };

    private const int HistoryMessages = 10;
    private const int MaxMessageChars = 400;
    private const int MaxSystemPromptChars = 500;

    private readonly BotBehaviorOptions _options = options?.Value ?? new();

    public async Task<List<BotVerdict>> DecideAsync(Guid chatId, Message message, List<Bot> bots, CancellationToken ct = default)
    {
        if (bots.Count == 0)
            return [];

        var keyed = bots.Select((bot, i) => (Key: $"bot_{i}", Bot: bot)).ToList();
        var state = await BuildStateAsync(chatId, message, keyed, ct);
        var questions = keyed.ToDictionary(k => k.Key, k => Question(k.Key));
        questions[HealthKey] = HealthQuestion;

        DecisionsResponse? response;
        try
        {
            response = await decisions.DecideAsync(state, questions, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Decisions call failed for message {MessageId}", message.Id);
            response = null;
        }

        // The health only holds back bots answering bots: a person is always
        // answered as usual, which is also what brings the bots back.
        var fromBot = message.SenderBotId is not null;
        var health = response?.Answers?.GetValueOrDefault(HealthKey) is { } healthAnswer ? Health(healthAnswer) : null;
        var band = fromBot && health is { } h
            ? h >= ranking.SilentHealth ? HealthBand.Silent : h >= ranking.CautiousHealth ? HealthBand.Cautious : HealthBand.Normal
            : HealthBand.Normal;
        var replyThreshold = band == HealthBand.Cautious ? Math.Max(ranking.ReplyThreshold, ranking.CautiousReplyThreshold) : ranking.ReplyThreshold;

        var decided = response?.Answers is { } answers
            ? keyed.Select(k => Map(k.Bot, answers.GetValueOrDefault(k.Key), replyThreshold)).ToList()
            : Fallback(message, bots);

        if (response?.Answers is not null)
            logger.LogInformation("Jev for message {MessageId}: health {Health} ({Band}; {BotMessages} bot messages since a person, {Seconds}s, humans active {HumansActive}); {Answers} (cost {Cost})",
                message.Id, health?.ToString("F2", CultureInfo.InvariantCulture) ?? "none", band, state.Flow.BotMessagesSinceLastHuman,
                state.Flow.SecondsSinceLastHuman, state.Flow.HumansActive,
                string.Join(", ", keyed.Select(k => Describe(k.Bot, response.Answers.GetValueOrDefault(k.Key)))),
                response.Usage?.Cost);

        decided = await RateCapAsync(chatId, decided, ct);

        // The safety cap: the likeliest repliers keep their turn. The health
        // band and the per-person budget can lower it.
        var budget = _options.MaxBotMessagesPerPersonMessage - state.Flow.BotMessagesSinceLastHuman;
        var max = Math.Max(0, Math.Min(Math.Min(ranking.MaxReplies, budget), band switch
        {
            HealthBand.Silent => 0,
            HealthBand.Cautious => ranking.CautiousMaxReplies,
            _ => int.MaxValue,
        }));
        var capped = decided.Where(d => d.Action == BotAction.Reply)
            .OrderByDescending(d => d.Probability)
            .Skip(max)
            .Select(d => d.Bot.Id)
            .ToHashSet();
        if (capped.Count > 0)
            logger.LogInformation("Reply cap of {Max} reached ({BotMessages} bot messages since a person wrote, health {Band}); {Count} bot(s) stay quiet",
                max, state.Flow.BotMessagesSinceLastHuman, band, capped.Count);

        return decided
            .Select(d => capped.Contains(d.Bot.Id) ? d with { Action = BotAction.Quiet, Emoji = null } : d)
            .ToList();
    }

    public static DecisionQuestion Question(string key) => new(
        "choice",
        $"What would the person in `bots.{key}`, going only by their persona, naturally do with `new_message` in this group chat?",
        Criteria);

    public const string HealthKey = "conversation_health";

    /// <summary>The levels of <see cref="HealthQuestion"/>, lowest (healthiest) first.</summary>
    public static readonly IReadOnlyList<string> HealthLevels =
    [
        "People are in the conversation and the bots add to it.",
        "The bots are mostly talking among themselves, but it is still on topic and fun to read.",
        "The bots are going in circles, repeating themselves, drifting off topic, or drowning out the people.",
    ];

    public static readonly DecisionQuestion HealthQuestion = new(
        "score",
        "How is this group chat going right now, judging `recent_messages`, `new_message` and `flow`?",
        HealthLevels);

    /// <summary>Jev's expected level, or the one its probabilities give when it left the score out.</summary>
    private static double? Health(DecisionAnswer answer) =>
        answer.Score ?? (answer.Probabilities is { Count: > 0 } p
            ? p.Sum(kv => double.TryParse(kv.Key, CultureInfo.InvariantCulture, out var level) ? level * kv.Value : 0)
            : null);

    private enum HealthBand
    {
        Normal,
        Cautious,
        Silent,
    }

    /// <summary>The chosen option, if its probability clears the threshold for its kind.</summary>
    private BotVerdict Map(Bot bot, DecisionAnswer? answer, double replyThreshold)
    {
        if (answer?.Choice is not { } choice)
            return new BotVerdict(bot, BotAction.Quiet);

        var p = answer.Probabilities?.GetValueOrDefault(choice) ?? answer.Confidence ?? 0;
        if (choice == Reply && p >= replyThreshold)
            return new BotVerdict(bot, BotAction.Reply, Probability: p);
        if (ranking.Emojis.TryGetValue(choice, out var emoji) && p >= ranking.ReactThreshold)
            return new BotVerdict(bot, BotAction.React, emoji, p);
        return new BotVerdict(bot, BotAction.Quiet, Probability: p);
    }

    /// <summary>
    /// Without Jev, something simple and predictable: the bots a person
    /// @mentioned or replied to answer, and nobody reacts. Nothing judges the
    /// conversation then, so a bot's message gets no bot replies at all.
    /// </summary>
    private List<BotVerdict> Fallback(Message message, List<Bot> bots)
    {
        var fromPerson = message.SenderBotId is null;
        var decided = bots.Select(bot => new BotVerdict(bot,
            fromPerson && (IsMentioned(message.Content, bot) || message.ReplyTo?.SenderBotId == bot.Id)
                ? BotAction.Reply
                : BotAction.Quiet)).ToList();
        logger.LogWarning("Jev unavailable for message {MessageId}; only mentioned or replied-to bots answer: {Bots}",
            message.Id, string.Join(", ", decided.Where(d => d.Action == BotAction.Reply).Select(d => d.Bot.Name)));
        return decided;
    }

    /// <summary>"@Rex", or "@" and one of the bot's aliases, as a whole word.</summary>
    public static bool IsMentioned(string content, Bot bot)
    {
        var names = new[] { bot.Name }.Concat((bot.Aliases ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return names.Any(name => name.Length > 0
            && Regex.IsMatch(content, $@"(?<!\w)@{Regex.Escape(name)}(?!\w)", RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Anti-spam: a bot that already sent <see cref="BotBehaviorOptions.MaxRepliesPerMinute"/>
    /// replies in the last minute sits this one out. It counts replies, not
    /// messages: only the first part of a split reply carries the reply link.
    /// </summary>
    private async Task<List<BotVerdict>> RateCapAsync(Guid chatId, List<BotVerdict> decided, CancellationToken ct)
    {
        var repliers = decided.Where(d => d.Action == BotAction.Reply).Select(d => d.Bot.Id).ToList();
        if (repliers.Count == 0)
            return decided;

        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        var counts = await db.Messages
            .Where(m => m.ChatId == chatId && m.SenderBotId != null && repliers.Contains(m.SenderBotId.Value)
                && m.ReplyToId != null && m.CreatedAt >= oneMinuteAgo)
            .GroupBy(m => m.SenderBotId!.Value)
            .Select(g => new { BotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.BotId, g => g.Count, ct);

        return decided.Select(d =>
        {
            if (d.Action != BotAction.Reply || counts.GetValueOrDefault(d.Bot.Id) < _options.MaxRepliesPerMinute)
                return d;
            logger.LogInformation("Bot {Bot}: rate capped ({Count} replies in the last minute)", d.Bot.Name, counts[d.Bot.Id]);
            return d with { Action = BotAction.Quiet };
        }).ToList();
    }

    private async Task<DecisionState> BuildStateAsync(Guid chatId, Message message, List<(string Key, Bot Bot)> keyed, CancellationToken ct)
    {
        var chatName = await db.Chats.Where(c => c.Id == chatId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        // What came before this message, not what the other bots are saying
        // about it right now.
        var recent = await db.Messages
            .Where(m => m.ChatId == chatId && m.Id != message.Id && m.CreatedAt <= message.CreatedAt)
            .OrderByDescending(m => m.CreatedAt)
            .Take(HistoryMessages)
            .Include(m => m.SenderUser)
            .Include(m => m.SenderBot)
            .Include(m => m.Attachments)
            .AsNoTracking()
            .ToListAsync(ct);
        recent.Reverse();

        return new DecisionState(
            chatName ?? "",
            recent.Select(Said).ToList(),
            Said(message) with { ReplyTo = message.ReplyTo is { } target ? SenderName(target) : null },
            await FlowAsync(chatId, ct),
            keyed.ToDictionary(k => k.Key, k => new DecisionBot(k.Bot.Name, Persona(k.Bot))));
    }

    /// <summary>
    /// How the chat is flowing right now. Every bot message since the last
    /// person's message counts, including replies still landing in parallel,
    /// so it doubles as the per-person budget for the safety net.
    /// </summary>
    private async Task<DecisionFlow> FlowAsync(Guid chatId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var lastHuman = await db.Messages
            .Where(m => m.ChatId == chatId && m.SenderUserId != null)
            .MaxAsync(m => (DateTime?)m.CreatedAt, ct);
        var botMessages = await db.Messages
            .CountAsync(m => m.ChatId == chatId && m.SenderBotId != null && (lastHuman == null || m.CreatedAt > lastHuman), ct);
        var seconds = lastHuman is { } at ? (int)Math.Max(0, (now - at).TotalSeconds) : (int?)null;
        return new DecisionFlow(botMessages, seconds, lastHuman >= now - ranking.HumansActiveWindow);
    }

    private static DecisionMessage Said(Message m) =>
        new(SenderName(m), m.SenderBotId is not null, BotPromptBuilder.WithImages(m, maxContentChars: MaxMessageChars));

    private static string SenderName(Message m) =>
        m.SenderUser?.DisplayName is { Length: > 0 } user ? user : m.SenderBot?.Name ?? "Unknown";

    private static string Persona(Bot bot) =>
        string.Join(" ", new[] { bot.Personality, Trim(bot.SystemPrompt, MaxSystemPromptChars) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string Trim(string? text, int max) =>
        text is null ? string.Empty : text.Length > max ? text[..max] + "..." : text;

    private static string Describe(Bot bot, DecisionAnswer? answer) =>
        answer?.Choice is { } choice
            ? $"{bot.Name}={choice} {answer.Probabilities?.GetValueOrDefault(choice) ?? answer.Confidence ?? 0:F2}"
            : $"{bot.Name}=no answer";
}

/// <summary>The <c>state</c> Jev reads, in the order it is sent.</summary>
public record DecisionState(
    string Chat,
    List<DecisionMessage> RecentMessages,
    DecisionMessage NewMessage,
    DecisionFlow Flow,
    Dictionary<string, DecisionBot> Bots);

/// <summary>
/// Computed by the code for the <c>conversation_health</c> question.
/// <paramref name="SecondsSinceLastHuman"/> is left out when no person has
/// written in the chat.
/// </summary>
public record DecisionFlow(int BotMessagesSinceLastHuman, int? SecondsSinceLastHuman, bool HumansActive);

/// <param name="ReplyTo">Who the message answers, when it is a reply; left out otherwise.</param>
public record DecisionMessage(string Sender, bool IsBot, string Text, string? ReplyTo = null);

public record DecisionBot(string Name, string Persona);
