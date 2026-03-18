using NeuralDamage.Application.Interfaces;
using NeuralDamage.Application.Services.BotDecision;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
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

    [Fact]
    public async Task MentionedBot_AlwaysResponds_SkipsTier2And3()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        using var _ = db;
        var openRouter = Substitute.For<IOpenRouterService>();
        var judge = new Tier3LlmJudge(openRouter);
        var engine = new BotDecisionEngine(db, judge);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "hey GPT what do you think?" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        // GPT was mentioned — must be in responders regardless of Tier 2/3
        Assert.Contains(bot1.Id, responders);
    }

    [Fact]
    public async Task GroupAddress_AllBotsRespond()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        using var _ = db;
        var openRouter = Substitute.For<IOpenRouterService>();
        var judge = new Tier3LlmJudge(openRouter);
        var engine = new BotDecisionEngine(db, judge);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "hey everyone what's your opinion?" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        Assert.Contains(bot1.Id, responders);
        Assert.Contains(bot2.Id, responders);
    }

    [Fact]
    public async Task BotToBotMessage_NoMention_NeitherResponds()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        using var _ = db;
        var openRouter = Substitute.For<IOpenRouterService>();
        var judge = new Tier3LlmJudge(openRouter);
        var engine = new BotDecisionEngine(db, judge);

        var msg = new Message { ChatId = chat.Id, SenderBotId = bot1.Id, Content = "I agree with that" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        Assert.DoesNotContain(bot1.Id, responders); // sender bot skipped
        Assert.DoesNotContain(bot2.Id, responders); // not mentioned
    }

    [Fact]
    public async Task InactiveBot_NeverResponds()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        using var _ = db;
        bot1.IsActive = false;
        await db.SaveChangesAsync();

        var openRouter = Substitute.For<IOpenRouterService>();
        var judge = new Tier3LlmJudge(openRouter);
        var engine = new BotDecisionEngine(db, judge);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "hey GPT respond please" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        Assert.DoesNotContain(bot1.Id, responders);
    }

    [Fact]
    public async Task AliasMentioned_BotResponds()
    {
        var (db, user, chat, bot1, bot2) = await SetupChatWithBots();
        using var _ = db;
        var openRouter = Substitute.For<IOpenRouterService>();
        var judge = new Tier3LlmJudge(openRouter);
        var engine = new BotDecisionEngine(db, judge);

        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "chatgpt help me out" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var responders = await engine.DecideRespondersAsync(chat.Id, msg, [bot1, bot2]);

        Assert.Contains(bot1.Id, responders);
    }
}
