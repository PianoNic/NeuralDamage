using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;
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

    private static IBotRankingService RankingReturning(string? reply)
    {
        var ranking = Substitute.For<IBotRankingService>();
        ranking.RankAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(reply);
        return ranking;
    }

    [Test]
    public async Task RankingUnavailable_FallsBackToBotsAboveTier2Threshold()
    {
        var above = MakeBot("Above");
        var below = MakeBot("Below");
        var judge = new Tier3LlmJudge(RankingReturning(null), NullLogger<Tier3LlmJudge>.Instance);

        var result = await judge.JudgeAsync(
            MakeMessage(), [(above, 0.9), (below, 0.1)], [], CancellationToken.None);

        await Assert.That(result).Contains(above.Id);
        await Assert.That(result).DoesNotContain(below.Id);
    }

    [Test]
    public async Task RankingThrows_FallsBackToBotsAboveTier2Threshold()
    {
        var ranking = Substitute.For<IBotRankingService>();
        ranking.RankAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new HttpRequestException("endpoint down"));

        var above = MakeBot("Above");
        var judge = new Tier3LlmJudge(ranking, NullLogger<Tier3LlmJudge>.Instance);

        var result = await judge.JudgeAsync(
            MakeMessage(), [(above, 0.75)], [], CancellationToken.None);

        await Assert.That(result).Contains(above.Id);
    }

    [Test]
    public async Task RankingReturnsResponders_SelectsThem()
    {
        var chosen = MakeBot("Chosen");
        var ignored = MakeBot("Ignored");
        var judge = new Tier3LlmJudge(
            RankingReturning($"{{\"responders\": [\"{chosen.Id}\"]}}"),
            NullLogger<Tier3LlmJudge>.Instance);

        // Both sit below the Tier 2 threshold, so a fallback would return neither.
        var result = await judge.JudgeAsync(
            MakeMessage(), [(chosen, 0.2), (ignored, 0.2)], [], CancellationToken.None);

        await Assert.That(result).Contains(chosen.Id);
        await Assert.That(result).DoesNotContain(ignored.Id);
    }

    [Test]
    public async Task RankingWrapsJsonInCodeFence_StillParses()
    {
        var chosen = MakeBot("Chosen");
        var judge = new Tier3LlmJudge(
            RankingReturning($"```json\n{{\"responders\": [\"{chosen.Id}\"]}}\n```"),
            NullLogger<Tier3LlmJudge>.Instance);

        var result = await judge.JudgeAsync(
            MakeMessage(), [(chosen, 0.2)], [], CancellationToken.None);

        await Assert.That(result).Contains(chosen.Id);
    }

    [Test]
    public async Task RankingReturnsUnparseableText_FallsBackToTier2()
    {
        var above = MakeBot("Above");
        var judge = new Tier3LlmJudge(RankingReturning("I think nobody should reply."), NullLogger<Tier3LlmJudge>.Instance);

        var result = await judge.JudgeAsync(
            MakeMessage(), [(above, 0.8)], [], CancellationToken.None);

        await Assert.That(result).Contains(above.Id);
    }

    [Test]
    public async Task RankingNamesUnknownBot_IdIsDiscarded()
    {
        var known = MakeBot("Known");
        var judge = new Tier3LlmJudge(
            RankingReturning($"{{\"responders\": [\"{Guid.NewGuid()}\"]}}"),
            NullLogger<Tier3LlmJudge>.Instance);

        var result = await judge.JudgeAsync(
            MakeMessage(), [(known, 0.2)], [], CancellationToken.None);

        await Assert.That(result).IsEmpty();
    }
}
