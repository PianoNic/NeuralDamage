using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

/// <summary>
/// Jev decides what every bot does with a message, in one call: one
/// <c>choice</c> question per bot, each judged on that bot's persona and the
/// message alone. The code only turns the probabilities into actions.
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
        var decided = response?.Answers is { } answers
            ? keyed.Select(k => Map(k.Bot, answers.GetValueOrDefault(k.Key))).ToList()
            : Fallback(message, bots);

        if (response?.Answers is not null)
            logger.LogInformation("Jev for message {MessageId}: {Answers} (cost {Cost})", message.Id,
                string.Join(", ", keyed.Select(k => Describe(k.Bot, response.Answers.GetValueOrDefault(k.Key)))),
                response.Usage?.Cost);

        decided = await RateCapAsync(chatId, decided, ct);

        // The safety cap: the likeliest repliers keep their turn.
        var capped = decided.Where(d => d.Action == BotAction.Reply)
            .OrderByDescending(d => d.Probability)
            .Skip(ranking.MaxReplies)
            .Select(d => d.Bot.Id)
            .ToHashSet();
        if (capped.Count > 0)
            logger.LogInformation("Reply cap of {Max} reached; {Count} bot(s) stay quiet", ranking.MaxReplies, capped.Count);

        return decided
            .Select(d => capped.Contains(d.Bot.Id) ? d with { Action = BotAction.Quiet, Emoji = null } : d)
            .ToList();
    }

    public static DecisionQuestion Question(string key) => new(
        "choice",
        $"What would the person in `bots.{key}`, going only by their persona, naturally do with `new_message` in this group chat?",
        Criteria);

    /// <summary>The chosen option, if its probability clears the threshold for its kind.</summary>
    private BotVerdict Map(Bot bot, DecisionAnswer? answer)
    {
        if (answer?.Choice is not { } choice)
            return new BotVerdict(bot, BotAction.Quiet);

        var p = answer.Probabilities?.GetValueOrDefault(choice) ?? answer.Confidence ?? 0;
        if (choice == Reply && p >= ranking.ReplyThreshold)
            return new BotVerdict(bot, BotAction.Reply, Probability: p);
        if (ranking.Emojis.TryGetValue(choice, out var emoji) && p >= ranking.ReactThreshold)
            return new BotVerdict(bot, BotAction.React, emoji, p);
        return new BotVerdict(bot, BotAction.Quiet, Probability: p);
    }

    /// <summary>
    /// Without Jev, something simple and predictable: the bots a person
    /// @mentioned or replied to answer, and nobody reacts.
    /// </summary>
    private List<BotVerdict> Fallback(Message message, List<Bot> bots)
    {
        var decided = bots.Select(bot => new BotVerdict(bot,
            IsMentioned(message.Content, bot)
            // Every bot reply links what it answered, so from a bot a reply
            // link means nothing; only a person replying picks a bot.
            || (message.SenderUserId is not null && message.ReplyTo?.SenderBotId == bot.Id)
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
            keyed.ToDictionary(k => k.Key, k => new DecisionBot(k.Bot.Name, Persona(k.Bot))));
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
    Dictionary<string, DecisionBot> Bots);

/// <param name="ReplyTo">Who the message answers, when it is a reply; left out otherwise.</param>
public record DecisionMessage(string Sender, bool IsBot, string Text, string? ReplyTo = null);

public record DecisionBot(string Name, string Persona);
