using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using static NeuralDamage.Infrastructure.Services.BotDecision.BotDecisionEngine;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>Reactions come from Jev's react choices, alongside the replies.</summary>
public class BotReactionOrchestrationTests
{
    [Test]
    public async Task ReactChoice_PutsTheMappedEmojiOnTheMessage_AndBroadcasts()
    {
        var jev = new FakeJev(name => name switch
        {
            "GPT" => FakeJev.Chose(ReactWow, 0.55),
            "Claude" => FakeJev.Chose(ReactLaugh, 0.8),
            _ => FakeJev.Chose(Quiet, 0.9),
        });
        using var h = await OrchestratorHarness.CreateAsync(botCount: 3, configure: jev.Engine());
        var message = await h.SayAsync("i saw dune twice today");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        var reactions = await h.Db.Reactions.AsNoTracking().ToListAsync();
        await Assert.That(reactions.Select(r => (r.BotId!.Value, r.Emoji))).IsEquivalentTo(
            [(h.Bots[0].Id, "😮"), (h.Bots[1].Id, "😂")]);
        await Assert.That(reactions.All(r => r.MessageId == message.Id && r.UserId == null)).IsTrue();
        await h.Notifications.Received(2).NotifyReactionUpdated(h.Chat.Id, message.Id, Arg.Any<List<ReactionGroupDto>>());
        // Reacting is all they do.
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifyMessageNew(default, default!);
        await h.OpenRouter.DidNotReceiveWithAnyArgs().GenerateResponseAsync(default!, default, default!, default!, default);
    }

    [Test]
    public async Task ConfiguredEmoji_IsUsed()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(ReactLove, 0.9));
        var emojis = BotRankingOptions.DefaultEmojis.ToDictionary(e => e.Key, e => e.Value);
        emojis[ReactLove] = "🥰";
        using var h = await OrchestratorHarness.CreateAsync(configure: jev.Engine(new BotRankingOptions { Emojis = emojis }));
        var message = await h.SayAsync("made you all cookies");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        await Assert.That((await h.Db.Reactions.SingleAsync()).Emoji).IsEqualTo("🥰");
    }

    [Test]
    public async Task RepliesAndReactions_HappenInTheSameRound()
    {
        var jev = new FakeJev(name => name == "GPT" ? FakeJev.Chose(Reply, 0.8) : FakeJev.Chose(ReactThumbs, 0.7));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2, configure: jev.Engine());
        h.Reply("dune part two was better");
        var message = await h.SayAsync("anyone seen the new dune movie?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        await Assert.That((await h.MessagesFromAsync(h.Bots[0])).Select(m => m.Content)).IsEquivalentTo(["dune part two was better"]);
        var reaction = await h.Db.Reactions.AsNoTracking().SingleAsync();
        await Assert.That(reaction.BotId).IsEqualTo(h.Bots[1].Id);
        await Assert.That(reaction.Emoji).IsEqualTo("👍");
    }

    [Test]
    public async Task QuietOrUnsure_NoReaction()
    {
        var jev = new FakeJev(name => name == "GPT" ? FakeJev.Chose(Quiet, 0.9) : FakeJev.Chose(ReactLaugh, 0.3));
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2, configure: jev.Engine());
        var message = await h.SayAsync("that is hilarious");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        await Assert.That(await h.Db.Reactions.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task MutedBot_NeitherReactsNorReplies()
    {
        var jev = new FakeJev(_ => FakeJev.Chose(ReactLaugh, 0.9));
        using var h = await OrchestratorHarness.CreateAsync(configure: jev.Engine());
        h.BotState.Mute(h.Chat.Id, h.Bots[0].Id);
        var message = await h.SayAsync("that is hilarious");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        await Assert.That(jev.Calls).IsEmpty();
        await Assert.That(await h.Db.Reactions.AnyAsync()).IsFalse();
    }
}
