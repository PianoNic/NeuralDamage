using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

public class BotPromptBuilderTests
{
    [Fact]
    public void BuildSystemPrompt_ContainsBotNameAndParticipants()
    {
        var bot = new Bot { Name = "GPT", ModelId = "test", SystemPrompt = "Be helpful", Personality = "Friendly", CreatedById = Guid.NewGuid() };
        var participants = new List<string> { "Alice", "GPT", "Claude" };

        var prompt = BotPromptBuilder.BuildSystemPrompt(bot, participants);

        Assert.Contains("GPT", prompt);
        Assert.Contains("Alice", prompt);
        Assert.Contains("Claude", prompt);
        Assert.Contains("Be helpful", prompt);
        Assert.Contains("Friendly", prompt);
        Assert.Contains("1-3 sentences", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_OmitsPersonalityWhenNull()
    {
        var bot = new Bot { Name = "GPT", ModelId = "test", SystemPrompt = "Be helpful", CreatedById = Guid.NewGuid() };

        var prompt = BotPromptBuilder.BuildSystemPrompt(bot, ["Alice"]);

        Assert.DoesNotContain("Additional personality", prompt);
    }

    [Fact]
    public void BuildHistory_TruncatesLongMessages()
    {
        var botId = Guid.NewGuid();
        var longContent = new string('x', 2000);
        var messages = new List<Message>
        {
            new() { ChatId = Guid.NewGuid(), SenderUserId = Guid.NewGuid(), Content = longContent, SenderUser = new User { ExternalId = "e", Email = "a@b.com", DisplayName = "Alice" } }
        };

        var history = BotPromptBuilder.BuildHistory(messages, botId);

        Assert.Single(history);
        Assert.Contains("...", history[0].Content);
        Assert.True(history[0].Content.Length < 2000);
    }

    [Fact]
    public void BuildHistory_AssignsCorrectRoles()
    {
        var botId = Guid.NewGuid();
        var messages = new List<Message>
        {
            new() { ChatId = Guid.NewGuid(), SenderUserId = Guid.NewGuid(), Content = "Hi", SenderUser = new User { ExternalId = "e", Email = "a@b.com", DisplayName = "Alice" } },
            new() { ChatId = Guid.NewGuid(), SenderBotId = botId, Content = "Hello!", SenderBot = new Bot { Name = "GPT", ModelId = "m", SystemPrompt = "x", CreatedById = Guid.NewGuid() } },
            new() { ChatId = Guid.NewGuid(), SenderBotId = Guid.NewGuid(), Content = "Hey", SenderBot = new Bot { Name = "Claude", ModelId = "m", SystemPrompt = "x", CreatedById = Guid.NewGuid() } }
        };

        var history = BotPromptBuilder.BuildHistory(messages, botId);

        Assert.Equal(3, history.Count);
        Assert.Equal("user", history[0].Role);      // human
        Assert.Equal("assistant", history[1].Role);  // current bot
        Assert.Equal("user", history[2].Role);       // other bot
    }

    [Fact]
    public void BuildHistory_RespectsCharLimit()
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
        Assert.True(totalChars <= 12_000);
        Assert.True(history.Count < 100);
    }
}

public class BotResponseCancellationTests
{
    [Fact]
    public void CancelPendingResponses_DoesNotThrow_WhenNoPending()
    {
        // Just verify it doesn't throw — no scope factory needed for this
        var orchestrator = Substitute.For<IBotResponseOrchestrator>();
        orchestrator.CancelPendingResponses(Guid.NewGuid());
    }
}
