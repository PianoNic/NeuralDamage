using System.Text.Json;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services.BotDecision;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace NeuralDamage.Tests.BotDecision;

public class Tier3LlmJudgeTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static Bot MakeBot(string name) =>
        new() { Name = name, ModelId = "m", SystemPrompt = "x", CreatedById = OwnerId };

    private static Message MakeMessage() =>
        new() { ChatId = Guid.NewGuid(), SenderUserId = OwnerId, Content = "anyone around?" };

    private static Tier3LlmJudge Judge(IDecisionsClient decisions) =>
        new(decisions, new BotRankingOptions(), NullLogger<Tier3LlmJudge>.Instance);

    /// <summary>A client answering each bot_i question with the i-th probability.</summary>
    private static IDecisionsClient DecisionsReturning(params double[] probabilities)
    {
        var answers = probabilities
            .Select((p, i) => (Key: $"bot_{i}", Answer: new DecisionAnswer("noul", p, null, null, null)))
            .ToDictionary(a => a.Key, a => a.Answer);

        var decisions = Substitute.For<IDecisionsClient>();
        decisions.DecideAsync(Arg.Any<object>(), Arg.Any<IReadOnlyDictionary<string, DecisionQuestion>>(), Arg.Any<CancellationToken>())
            .Returns(new DecisionsResponse("gen-dec-1", "typesafe/jev", answers, new DecisionUsage(400, 10, 0.00002m)));
        return decisions;
    }

    [Test]
    public async Task DecisionsUnavailable_FallsBackToBotsAboveTier2Threshold()
    {
        var above = MakeBot("Above");
        var below = MakeBot("Below");
        var decisions = Substitute.For<IDecisionsClient>();
        decisions.DecideAsync(Arg.Any<object>(), Arg.Any<IReadOnlyDictionary<string, DecisionQuestion>>(), Arg.Any<CancellationToken>())
            .Returns((DecisionsResponse?)null);

        var result = await Judge(decisions).JudgeAsync(
            MakeMessage(), [(above, 0.9), (below, 0.1)], [], CancellationToken.None);

        await Assert.That(result).Contains(above.Id);
        await Assert.That(result).DoesNotContain(below.Id);
    }

    [Test]
    public async Task DecisionsThrows_FallsBackToBotsAboveTier2Threshold()
    {
        var decisions = Substitute.For<IDecisionsClient>();
        decisions.DecideAsync(Arg.Any<object>(), Arg.Any<IReadOnlyDictionary<string, DecisionQuestion>>(), Arg.Any<CancellationToken>())
            .Returns<Task<DecisionsResponse?>>(_ => throw new HttpRequestException("endpoint down"));

        var above = MakeBot("Above");

        var result = await Judge(decisions).JudgeAsync(
            MakeMessage(), [(above, 0.75)], [], CancellationToken.None);

        await Assert.That(result).Contains(above.Id);
    }

    [Test]
    public async Task ProbabilityAboveThreshold_Responds_BelowDoesNot()
    {
        var chosen = MakeBot("Chosen");
        var ignored = MakeBot("Ignored");

        // Both sit below the Tier 2 threshold, so a fallback would return neither.
        var result = await Judge(DecisionsReturning(0.93, 0.2)).JudgeAsync(
            MakeMessage(), [(chosen, 0.2), (ignored, 0.2)], [], CancellationToken.None);

        await Assert.That(result).Contains(chosen.Id);
        await Assert.That(result).DoesNotContain(ignored.Id);
    }

    [Test]
    public async Task ManyAboveThreshold_OnlyTopMaxRespondersReply()
    {
        var low = MakeBot("Low");
        var high = MakeBot("High");
        var mid = MakeBot("Mid");

        var result = await Judge(DecisionsReturning(0.65, 0.95, 0.8)).JudgeAsync(
            MakeMessage(), [(low, 0.5), (high, 0.5), (mid, 0.5)], [], CancellationToken.None);

        await Assert.That(result).IsEquivalentTo(new[] { high.Id, mid.Id });
    }

    [Test]
    public async Task AnswerMissingForBot_BotStaysSilent()
    {
        var answered = MakeBot("Answered");
        var missing = MakeBot("Missing");

        var result = await Judge(DecisionsReturning(0.9)).JudgeAsync(
            MakeMessage(), [(answered, 0.9), (missing, 0.9)], [], CancellationToken.None);

        await Assert.That(result).IsEquivalentTo(new[] { answered.Id });
    }

    [Test]
    public async Task State_CarriesSenderNamesAndBotFlag()
    {
        var grumpy = MakeBot("Grumpy");
        grumpy.Personality = "sarcastic film critic";
        var alice = new User { ExternalId = "ext", Email = "a@a", DisplayName = "Alice" };
        var message = new Message { ChatId = Guid.NewGuid(), SenderUserId = alice.Id, SenderUser = alice, Content = "anyone seen the new dune?" };
        var history = new List<Message>
        {
            new() { ChatId = message.ChatId, SenderBotId = grumpy.Id, SenderBot = grumpy, Content = "movies peaked in 1974" },
            message,
        };

        object? captured = null;
        IReadOnlyDictionary<string, DecisionQuestion>? questions = null;
        var decisions = Substitute.For<IDecisionsClient>();
        decisions.DecideAsync(Arg.Do<object>(s => captured = s), Arg.Do<IReadOnlyDictionary<string, DecisionQuestion>>(q => questions = q), Arg.Any<CancellationToken>())
            .Returns((DecisionsResponse?)null);

        await Judge(decisions).JudgeAsync(message, [(grumpy, 0.5)], history, CancellationToken.None);

        using var state = JsonDocument.Parse(JsonSerializer.Serialize(captured, DecisionsClient.JsonOptions));
        var recent = state.RootElement.GetProperty("recent_messages");
        await Assert.That(recent.GetArrayLength()).IsEqualTo(1); // the new message is not repeated as history
        await Assert.That(recent[0].GetProperty("sender").GetString()).IsEqualTo("Grumpy");
        await Assert.That(recent[0].GetProperty("is_bot").GetBoolean()).IsTrue();
        await Assert.That(state.RootElement.GetProperty("new_message").GetProperty("sender").GetString()).IsEqualTo("Alice");
        var bot = state.RootElement.GetProperty("bots").GetProperty("bot_0");
        await Assert.That(bot.GetProperty("name").GetString()).IsEqualTo("Grumpy");
        await Assert.That(bot.GetProperty("persona").GetString()!).Contains("sarcastic film critic");
        await Assert.That(questions!["bot_0"].Type).IsEqualTo("noul");
    }
}
