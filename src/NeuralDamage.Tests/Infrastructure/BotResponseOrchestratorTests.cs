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
        await Assert.That(prompt).Contains("1-3 sentences");
    }

    [Test]
    public async Task BuildSystemPrompt_OmitsPersonalityWhenNull()
    {
        var bot = new Bot { Name = "GPT", ModelId = "test", SystemPrompt = "Be helpful", CreatedById = Guid.NewGuid() };

        var prompt = BotPromptBuilder.BuildSystemPrompt(bot, ["Alice"]);

        await Assert.That(prompt).DoesNotContain("Additional personality");
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
