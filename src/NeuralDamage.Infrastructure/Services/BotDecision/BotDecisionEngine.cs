using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

public class BotDecisionEngine(
    NeuralDamageDbContext db,
    Tier3LlmJudge tier3Judge,
    IChatBotState botState,
    ILogger<BotDecisionEngine> logger,
    IOptions<BotBehaviorOptions>? options = null) : IBotDecisionEngine
{
    // Priorities for the responder cap. Being named or replied to beats being
    // one of "everyone", which beats any Tier 2/3 score (those stay in 0..1).
    private const double AddressedPriority = 3.0;
    private const double GroupPriority = 2.0;

    private readonly BotBehaviorOptions _options = options?.Value ?? new();

    public async Task<List<Guid>> DecideRespondersAsync(Guid chatId, Message message, List<Bot> candidateBots, CancellationToken ct = default)
    {
        var mustRespond = new List<(Guid BotId, double Priority)>();
        var groupAddressed = new List<Bot>();
        var undecided = new List<(Bot Bot, double Score)>();

        // Load context for Tier 2
        var recentMessages = await db.Messages
            .Where(m => m.ChatId == chatId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(20)
            .Include(m => m.SenderUser)
            .Include(m => m.SenderBot)
            .AsNoTracking()
            .ToListAsync(ct);

        var totalBotsInChat = candidateBots.Count;
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        // The two messages just before this one, for the continuation bonus.
        var justBefore = recentMessages
            .Where(m => m.Id != message.Id && m.CreatedAt <= message.CreatedAt)
            .Take(2)
            .ToList();

        // A person talking to another person by name ("hey alice, ...") is not
        // talking to the bots, however recently one of them spoke.
        var otherPeople = message.SenderUserId is null
            ? []
            : await db.ChatMembers
                .Where(cm => cm.ChatId == chatId && cm.UserId != null && cm.UserId != message.SenderUserId)
                .Select(cm => cm.User!.DisplayName)
                .ToListAsync(ct);
        var toAPerson = otherPeople.Any(name => FuzzyNameMatcher.IsNameMentioned(message.Content, name, null));

        foreach (var bot in candidateBots)
        {
            // Tier 1: Hard rules
            var tier1 = Tier1HardRules.Evaluate(message, bot, isMuted: botState.IsMuted(chatId, bot.Id), isStopped: botState.IsStopped(chatId));
            if (tier1 != Tier1Result.Undecided)
                logger.LogInformation("Bot {Bot}: tier 1 says {Tier1}", bot.Name, tier1);
            if (tier1 == Tier1Result.MustSkip) continue;

            // Anti-spam applies however the bot was addressed: it is what keeps
            // a live back-and-forth from turning into a flood. It counts replies,
            // not messages - a reply split in three is one turn, and only its
            // first part carries the reply link.
            var repliesLastMinute = recentMessages.Count(m =>
                m.SenderBotId == bot.Id && m.ReplyToId is not null && m.CreatedAt >= oneMinuteAgo);
            if (repliesLastMinute >= _options.MaxRepliesPerMinute)
            {
                logger.LogInformation("Bot {Bot}: rate capped ({Count} replies in the last minute)", bot.Name, repliesLastMinute);
                continue;
            }

            if (tier1 == Tier1Result.MustRespond) { mustRespond.Add((bot.Id, AddressedPriority)); continue; }
            if (tier1 == Tier1Result.GroupAddressed) { groupAddressed.Add(bot); continue; }

            // Another bot's message that does not name this one: now and then
            // it chimes in anyway, but Tiers 2 and 3 are for people's messages.
            if (message.SenderBotId is not null)
            {
                if (Random.Shared.NextDouble() < _options.BotChainChance)
                    mustRespond.Add((bot.Id, 0));
                continue;
            }

            if (toAPerson)
            {
                logger.LogInformation("Bot {Bot}: message is addressed to another person; skipping", bot.Name);
                continue;
            }

            // Tier 2: Weighted score
            var botMessagesInLast20 = recentMessages.Count(m => m.SenderBotId == bot.Id);

            var context = new Tier2Context(
                IsGroupQuestion: message.Content.Contains('?'),
                BotMessagesInLast20: botMessagesInLast20,
                TotalRecentMessages: recentMessages.Count,
                // The bot just spoke and a person answered: it is in the conversation.
                IsContinuation: message.SenderUserId is not null && justBefore.Any(m => m.SenderBotId == bot.Id),
                MessageLength: message.Content.Length,
                TotalBotsInChat: totalBotsInChat);

            var score = Tier2WeightedScore.ComputeScore(context);
            logger.LogInformation(
                "Bot {Bot}: tier 2 score {Score:F2} (respond >= {Respond}, skip < {Skip})",
                bot.Name, score, Tier2WeightedScore.RespondThreshold, Tier2WeightedScore.SkipThreshold);

            if (score >= Tier2WeightedScore.RespondThreshold) { mustRespond.Add((bot.Id, score)); continue; }
            if (score < Tier2WeightedScore.SkipThreshold) continue;

            undecided.Add((bot, score));
        }

        // Said to the room: some of it answers, not all of it.
        if (groupAddressed.Count > 0)
        {
            var count = Random.Shared.Next(1, groupAddressed.Count + 1);
            mustRespond.AddRange(groupAddressed
                .OrderBy(_ => Random.Shared.Next())
                .Take(count)
                .Select(b => (b.Id, GroupPriority)));
        }

        // Tier 3 exists to choose between bots. With a single bot in the chat
        // there is nothing to disambiguate, so asking a model "which of these
        // one bots should reply?" only adds a round trip and a chance of an
        // unexplained silence. Tier 2 has already had its say via SkipThreshold.
        if (undecided.Count > 0 && totalBotsInChat == 1)
        {
            logger.LogInformation("Single bot in chat; responding without a tier 3 call");
            mustRespond.AddRange(undecided.Select(u => (u.Bot.Id, u.Score)));
            undecided.Clear();
        }

        // Tier 3: Single Jev call for all undecided bots - unless the cap is
        // already full, in which case nobody it picks could answer anyway.
        if (undecided.Count > 0 && mustRespond.Count < _options.MaxRespondersPerMessage)
        {
            var history = recentMessages.OrderBy(m => m.CreatedAt).ToList();

            var judged = await tier3Judge.JudgeAsync(message, undecided, history, ct);
            logger.LogInformation("Tier 3 judged {Judged} of {Undecided} undecided bots as responders",
                judged.Count, undecided.Count);
            mustRespond.AddRange(undecided.Where(u => judged.Contains(u.Bot.Id)).Select(u => (u.Bot.Id, u.Score)));
        }

        // Never more than a couple of bots on one message: the strongest
        // claims win, ties broken at random.
        var responders = mustRespond
            .OrderByDescending(r => r.Priority)
            .ThenBy(_ => Random.Shared.Next())
            .Take(_options.MaxRespondersPerMessage)
            .Select(r => r.BotId)
            .ToList();

        logger.LogInformation("Decision for chat {ChatId}: {Count} responder(s) of {Candidates} wanting to",
            chatId, responders.Count, mustRespond.Count);
        return responders;
    }
}
