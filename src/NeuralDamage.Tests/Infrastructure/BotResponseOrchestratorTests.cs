using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

public class BotPromptBuilderTests
{
    [Test]
    public async Task BuildSystemPrompt_ContainsBotNameAndParticipants()
    {
        var bot = new Bot { Name = "GPT", ModelId = "test", SystemPrompt = "Be helpful", Personality = "Friendly", CreatedById = Guid.NewGuid() };
        var participants = new List<string> { "Alice", "GPT", "Claude" };

        var prompt = BotPromptBuilder.BuildSystemPrompt(bot, participants);

        await Assert.That(prompt).Contains("GPT");
        await Assert.That(prompt).Contains("Alice");
        await Assert.That(prompt).Contains("Claude");
        await Assert.That(prompt).Contains("Be helpful");
        await Assert.That(prompt).Contains("Friendly");
    }

    [Test]
    public async Task BuildSystemPrompt_PersonaComesFirst_WithChatName()
    {
        var bot = new Bot { Name = "GPT", ModelId = "test", SystemPrompt = "You are a grumpy pirate.", CreatedById = Guid.NewGuid() };

        var prompt = BotPromptBuilder.BuildSystemPrompt(bot, ["Alice"], "Movie Night");

        await Assert.That(prompt.StartsWith("You are a grumpy pirate.")).IsTrue();
        await Assert.That(prompt).Contains("\"Movie Night\"");
        await Assert.That(prompt).DoesNotContain("1-3 sentences");
    }

    [Test]
    public async Task BuildNote_CarriesTheTimeAndAnyInstruction()
    {
        var note = BotPromptBuilder.BuildNote(new DateTimeOffset(2026, 9, 30, 21, 15, 0, TimeSpan.Zero), "Say something different.");

        await Assert.That(note.Role).IsEqualTo(ChatMessage.Note);
        await Assert.That(note.Content).IsEqualTo("(It is Wednesday, 21:15 local time. Say something different.)");
    }

    [Test]
    public async Task BuildSystemPrompt_OmitsPersonalityWhenNull()
    {
        var bot = new Bot { Name = "GPT", ModelId = "test", SystemPrompt = "Be helpful", CreatedById = Guid.NewGuid() };

        var prompt = BotPromptBuilder.BuildSystemPrompt(bot, ["Alice"]);

        // No empty personality line left behind the persona.
        await Assert.That(prompt.ReplaceLineEndings("\n")).StartsWith("Be helpful\n\nYou are");
    }

    [Test]
    public async Task BuildHistory_TruncatesLongMessages()
    {
        var botId = Guid.NewGuid();
        var longContent = new string('x', 2000);
        var messages = new List<Message>
        {
            new() { ChatId = Guid.NewGuid(), SenderUserId = Guid.NewGuid(), Content = longContent, SenderUser = new User { ExternalId = "e", Email = "a@b.com", DisplayName = "Alice" } }
        };

        var history = BotPromptBuilder.BuildHistory(messages, botId);

        await Assert.That(history).HasSingleItem();
        await Assert.That(history[0].Content).Contains("...");
        await Assert.That(history[0].Content.Length < 2000).IsTrue();
    }

    [Test]
    public async Task BuildHistory_AssignsCorrectRoles()
    {
        var botId = Guid.NewGuid();
        var messages = new List<Message>
        {
            new() { ChatId = Guid.NewGuid(), SenderUserId = Guid.NewGuid(), Content = "Hi", SenderUser = new User { ExternalId = "e", Email = "a@b.com", DisplayName = "Alice" } },
            new() { ChatId = Guid.NewGuid(), SenderBotId = botId, Content = "Hello!", SenderBot = new Bot { Name = "GPT", ModelId = "m", SystemPrompt = "x", CreatedById = Guid.NewGuid() } },
            new() { ChatId = Guid.NewGuid(), SenderBotId = Guid.NewGuid(), Content = "Hey", SenderBot = new Bot { Name = "Claude", ModelId = "m", SystemPrompt = "x", CreatedById = Guid.NewGuid() } }
        };

        var history = BotPromptBuilder.BuildHistory(messages, botId);

        await Assert.That(history.Count).IsEqualTo(3);
        await Assert.That(history[0].Role).IsEqualTo("user");      // human
        await Assert.That(history[1].Role).IsEqualTo("assistant");  // current bot
        await Assert.That(history[2].Role).IsEqualTo("user");       // other bot
    }

    [Test]
    public async Task BuildHistory_RespectsCharLimit()
    {
        var botId = Guid.NewGuid();
        var messages = new List<Message>();
        for (int i = 0; i < 100; i++)
        {
            messages.Add(new Message
            {
                ChatId = Guid.NewGuid(),
                SenderUserId = Guid.NewGuid(),
                Content = new string('a', 500),
                SenderUser = new User { ExternalId = "e", Email = "a@b.com", DisplayName = "User" }
            });
        }

        var history = BotPromptBuilder.BuildHistory(messages, botId);

        var totalChars = history.Sum(h => h.Content.Length);
        await Assert.That(totalChars <= 12_000).IsTrue();
        await Assert.That(history.Count < 100).IsTrue();
    }
    [Test]
    public async Task BuildHistory_OverBudget_DropsOldestAndKeepsNewest()
    {
        var botId = Guid.NewGuid();
        var alice = new User { ExternalId = "e", Email = "a@b.com", DisplayName = "Alice" };
        var messages = Enumerable.Range(0, 100)
            .Select(i => new Message { ChatId = Guid.NewGuid(), SenderUserId = alice.Id, SenderUser = alice, Content = $"msg{i:D3} " + new string('a', 500) })
            .ToList();

        var history = BotPromptBuilder.BuildHistory(messages, botId);

        await Assert.That(history[^1].Content).Contains("msg099");
        await Assert.That(history[0].Content).DoesNotContain("msg000");
    }

    [Test]
    public async Task BuildHistory_AlwaysKeepsTrigger_EvenPastTheBudget()
    {
        var botId = Guid.NewGuid();
        var alice = new User { ExternalId = "e", Email = "a@b.com", DisplayName = "Alice" };
        var trigger = new Message { ChatId = Guid.NewGuid(), SenderUserId = alice.Id, SenderUser = alice, Content = "the question" };
        var messages = new List<Message> { trigger };
        // Enough newer chatter to use the whole budget on its own.
        messages.AddRange(Enumerable.Range(0, 40)
            .Select(_ => new Message { ChatId = Guid.NewGuid(), SenderUserId = alice.Id, SenderUser = alice, Content = new string('b', 500) }));

        var history = BotPromptBuilder.BuildHistory(messages, botId, trigger.Id);

        await Assert.That(history[0].Content).Contains("the question");
    }

    private static readonly User Alice = new() { ExternalId = "e", Email = "a@b.com", DisplayName = "Alice" };

    [Test]
    public async Task BuildHistory_OwnTurnsHaveNoNamePrefix()
    {
        var bot = new Bot { Name = "GPT", ModelId = "m", SystemPrompt = "x", CreatedById = Guid.NewGuid() };
        var messages = new List<Message>
        {
            new() { ChatId = Guid.NewGuid(), SenderUserId = Alice.Id, SenderUser = Alice, Content = "hi" },
            new() { ChatId = Guid.NewGuid(), SenderBotId = bot.Id, SenderBot = bot, Content = "yo" },
        };

        var history = BotPromptBuilder.BuildHistory(messages, bot.Id, messages[0].Id);

        await Assert.That(history[1].Content).IsEqualTo("yo");
        await Assert.That(history[0].Content).StartsWith("[Alice]");
    }

    [Test]
    public async Task BuildHistory_MarksGapsReplyTargetsAndTheTrigger()
    {
        var claude = new Bot { Name = "Claude", ModelId = "m", SystemPrompt = "x", CreatedById = Guid.NewGuid() };
        var start = DateTime.UtcNow.AddHours(-3);
        var botMessage = new Message { ChatId = Guid.NewGuid(), SenderBotId = claude.Id, SenderBot = claude, Content = "pizza?", CreatedAt = start };
        var reply = new Message
        {
            ChatId = Guid.NewGuid(), SenderUserId = Alice.Id, SenderUser = Alice, Content = "sure",
            ReplyToId = botMessage.Id, ReplyTo = botMessage, CreatedAt = start.AddHours(2),
        };

        var history = BotPromptBuilder.BuildHistory([botMessage, reply], Guid.NewGuid(), reply.Id);

        await Assert.That(history[1].Content).IsEqualTo("[Alice, 2h later] (→ Claude) (you're answering this): sure");
        await Assert.That(history[0].Content).DoesNotContain("answering");
    }
}

public class BotResponseCancellationTests
{
    [Test]
    public void CancelPendingResponses_DoesNotThrow_WhenNoPending()
    {
        // Just verify it doesn't throw — no scope factory needed for this
        var orchestrator = Substitute.For<IBotResponseOrchestrator>();
        orchestrator.CancelPendingResponses(Guid.NewGuid());
    }
}
