using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace NeuralDamage.Tests.BotDecision;

public class BotDecisionEngineTests
{
    private static async Task<(NeuralDamage.Infrastructure.NeuralDamageDbContext db, User user, Chat chat, Bot bot1, Bot bot2)> SetupChatWithBots()
    {
        var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Tester" };
        db.Users.Add(user);
        var chat = new Chat { Name = "General", CreatedById = user.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });

        var bot1 = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "Be helpful", CreatedById = user.Id, Aliases = "chatgpt" };
        var bot2 = new Bot { Name = "Claude", ModelId = "anthropic/claude-3.5-sonnet", SystemPrompt = "Be thoughtful", CreatedById = user.Id };
        db.Bots.AddRange(bot1, bot2);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, BotId = bot1.Id });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, BotId = bot2.Id });
        await db.SaveChangesAsync();
        return (db, user, chat, bot1, bot2);
    }

    [Test]
    public async Task MentionedBot_AlwaysResponds_SkipsTier2And3()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        using var _ = db;
        var decisions = Substitute.For<IDecisionsClient>();
        var judge = new Tier3LlmJudge(decisions, new BotRankingOptions(), NullLogger<Tier3LlmJudge>.Instance);
        var engine = new BotDecisionEngine(db, judge, new ChatBotState(), NullLogger<BotDecisionEngine>.Instance);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "hey GPT what do you think?" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        // GPT was mentioned — must be in responders regardless of Tier 2/3
        await Assert.That(responders).Contains(bot1.Id);
    }

    [Test]
    public async Task GroupAddress_SomeButNotAllBotsRespond()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        var bot3 = new Bot { Name = "Gemini", ModelId = "google/gemini", SystemPrompt = "x", CreatedById = user.Id };
        db.Bots.Add(bot3);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, BotId = bot3.Id });
        var engine = EngineWithSilentJudge(db);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "hey everyone what's your opinion?" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var counts = new HashSet<int>();
        var answered = new HashSet<Guid>();
        for (var i = 0; i < 100; i++)
        {
            var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2, bot3]);
            counts.Add(responders.Count);
            answered.UnionWith(responders);
        }

        // One or two answer - never the whole room - and not always the same bot.
        await Assert.That(counts.All(c => c is 1 or 2)).IsTrue();
        await Assert.That(counts).Contains(1);
        await Assert.That(answered.Count).IsEqualTo(3);
    }

    [Test]
    public async Task SomeoneInAStatement_IsNotGroupAddress()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        var engine = EngineWithSilentJudge(db);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "someone told me it rains tomorrow" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        await Assert.That(responders).IsEmpty();
    }

    [Test]
    public async Task EveryBotNamed_CappedAtTwoResponders()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        var bot3 = new Bot { Name = "Gemini", ModelId = "google/gemini", SystemPrompt = "x", CreatedById = user.Id };
        db.Bots.Add(bot3);
        var engine = EngineWithSilentJudge(db);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "GPT Claude Gemini, what do you think?" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2, bot3]);

        await Assert.That(responders.Count).IsEqualTo(2);
    }

    [Test]
    public async Task NamedBotOverRateCap_SitsItOut()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        var engine = EngineWithSilentJudge(db);
        for (var i = 0; i < 4; i++)
            db.Messages.Add(new Message { ChatId = chat.Id, SenderBotId = bot1.Id, Content = $"reply {i}", CreatedAt = DateTime.UtcNow.AddSeconds(-50 + i) });

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "GPT, one more thing?" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        await Assert.That(responders).DoesNotContain(bot1.Id);
    }

    [Test]
    public async Task BotThatJustSpoke_KeepsTheConversationGoing()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        var engine = EngineWithSilentJudge(db);
        db.Messages.Add(new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "what should I cook tonight", CreatedAt = DateTime.UtcNow.AddSeconds(-20) });
        db.Messages.Add(new Message { ChatId = chat.Id, SenderBotId = bot2.Id, Content = "risotto, obviously", CreatedAt = DateTime.UtcNow.AddSeconds(-10) });

        // No name, no question: only the fact that Claude just spoke points at it.
        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "hmm I have never made that before" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        await Assert.That(responders).IsEquivalentTo([bot2.Id]);
    }

    /// <summary>A Tier 3 that never picks anyone, so only Tiers 1 and 2 decide.</summary>
    private static BotDecisionEngine EngineWithSilentJudge(NeuralDamage.Infrastructure.NeuralDamageDbContext db)
    {
        // Jev answers with no probabilities at all: every undecided bot counts as 0.
        var decisions = Substitute.For<IDecisionsClient>();
        decisions.DecideAsync(Arg.Any<object>(), Arg.Any<IReadOnlyDictionary<string, DecisionQuestion>>(), Arg.Any<CancellationToken>())
            .Returns(new DecisionsResponse(null, null, [], null));
        var judge = new Tier3LlmJudge(decisions, new BotRankingOptions(), NullLogger<Tier3LlmJudge>.Instance);
        return new BotDecisionEngine(db, judge, new ChatBotState(), NullLogger<BotDecisionEngine>.Instance);
    }

    [Test]
    public async Task BotToBotMessage_NoMention_NeitherResponds()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        var decisions = Substitute.For<IDecisionsClient>();
        var judge = new Tier3LlmJudge(decisions, new BotRankingOptions(), NullLogger<Tier3LlmJudge>.Instance);
        var engine = new BotDecisionEngine(db, judge, new ChatBotState(), NullLogger<BotDecisionEngine>.Instance);

        var msg = new Message { ChatId = chat.Id, SenderBotId = bot1.Id, Content = "I agree with that" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        await Assert.That(responders).DoesNotContain(bot1.Id); // sender bot skipped
        await Assert.That(responders).DoesNotContain(bot2.Id); // not mentioned
    }

    [Test]
    public async Task InactiveBot_NeverResponds()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        bot1.IsActive = false;
        await db.SaveChangesAsync();

        var decisions = Substitute.For<IDecisionsClient>();
        var judge = new Tier3LlmJudge(decisions, new BotRankingOptions(), NullLogger<Tier3LlmJudge>.Instance);
        var engine = new BotDecisionEngine(db, judge, new ChatBotState(), NullLogger<BotDecisionEngine>.Instance);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "hey GPT respond please" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        await Assert.That(responders).DoesNotContain(bot1.Id);
    }

    [Test]
    public async Task AliasMentioned_BotResponds()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        var decisions = Substitute.For<IDecisionsClient>();
        var judge = new Tier3LlmJudge(decisions, new BotRankingOptions(), NullLogger<Tier3LlmJudge>.Instance);
        var engine = new BotDecisionEngine(db, judge, new ChatBotState(), NullLogger<BotDecisionEngine>.Instance);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "chatgpt help me out" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        await Assert.That(responders).Contains(bot1.Id);
    }

    [Test]
    public async Task MutedBot_IgnoresEvenAGroupAddress()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        using var _ = db;
        var botState = new ChatBotState();
        botState.Mute(chat.Id, bot1.Id);
        var judge = new Tier3LlmJudge(Substitute.For<IDecisionsClient>(), new BotRankingOptions(), NullLogger<Tier3LlmJudge>.Instance);
        var engine = new BotDecisionEngine(db, judge, botState, NullLogger<BotDecisionEngine>.Instance);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "hey everyone, GPT too" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        await Assert.That(responders).DoesNotContain(bot1.Id);
        await Assert.That(responders).Contains(bot2.Id);
    }

    [Test]
    public async Task StoppedChat_NobodyResponds()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        using var _ = db;
        var botState = new ChatBotState();
        botState.Stop(chat.Id);
        var judge = new Tier3LlmJudge(Substitute.For<IDecisionsClient>(), new BotRankingOptions(), NullLogger<Tier3LlmJudge>.Instance);
        var engine = new BotDecisionEngine(db, judge, botState, NullLogger<BotDecisionEngine>.Instance);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "hey everyone" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        await Assert.That(responders).IsEmpty();
    }
}
