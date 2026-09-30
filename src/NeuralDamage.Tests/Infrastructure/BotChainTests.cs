using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuralDamage.Infrastructure.BackgroundServices;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>Bots answering bots: how far a chain may run, and what stops it.</summary>
public class BotChainTests
{
    /// <summary>
    /// Bots that reply to every message they are asked about, so only the hop
    /// limit, the rate cap and a person speaking can end the chain.
    /// </summary>
    private static async Task<(OrchestratorHarness H, BotResponseQueue Queue, FakeJev Jev)> ChattyBotsAsync(
        int botCount = 2, BotBehaviorOptions? options = null, string replyPrefix = "")
    {
        var queue = new BotResponseQueue();
        var jev = new FakeJev(_ => FakeJev.Chose(BotDecisionEngine.Reply, 0.9));
        var h = await OrchestratorHarness.CreateAsync(botCount: botCount, options: options is null ? null : Options.Create(options), configure: s =>
        {
            s.AddSingleton<IBotResponseQueue>(queue);
            jev.Engine()(s);
        });
        var n = 0;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default)
            .ReturnsForAnyArgs(_ => $"{replyPrefix}take number {Interlocked.Increment(ref n)}, on a whole new subject");
        return (h, queue, jev);
    }

    /// <summary>No hop limit or rate cap to speak of, so only the health and the ceiling are left.</summary>
    private static BotBehaviorOptions Unlimited()
    {
        var options = InstantBotOptions.Create().Value;
        options.MaxBotChainDepth = 1000;
        options.MaxRepliesPerMinute = 1000;
        return options;
    }

    [Test]
    public async Task Ceiling_StopsAChainAtTenBotMessages_AcrossHops()
    {
        var (h, queue, jev) = await ChattyBotsAsync(botCount: 3, options: Unlimited());
        using var disposable = h;
        jev.Health = _ => 0; // Jev wrongly thinks it is all going fine.
        var trigger = await h.SayAsync("what's the best pizza topping?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);
        var depths = await DrainAsync(h, queue);

        await Assert.That(await BotMessagesAsync(h)).IsEqualTo(10);
        await Assert.That(depths.Max()).IsGreaterThan(1);
    }

    [Test]
    public async Task SpirallingHealth_EndsTheChain_AndAPersonBringsTheBotsBack()
    {
        var (h, queue, jev) = await ChattyBotsAsync(options: Unlimited());
        using var disposable = h;
        // Fine while a person leads; a spiral from the second bot message on.
        jev.Health = s => s.Flow.BotMessagesSinceLastHuman >= 2 ? 1.8 : 0;
        var trigger = await h.SayAsync("what's the best pizza topping?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);
        await DrainAsync(h, queue);
        await Assert.That(await BotMessagesAsync(h)).IsEqualTo(2);

        var again = await h.SayAsync("ok but what about sushi?");
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, again.Id);
        await DrainAsync(h, queue);

        await Assert.That(await BotMessagesAsync(h)).IsEqualTo(4);
        var personCall = jev.Calls.Last(c => !c.State.NewMessage.IsBot);
        await Assert.That(personCall.State.Flow.BotMessagesSinceLastHuman).IsEqualTo(0);
    }

    [Test]
    public async Task JevUnavailable_BotMessagesNeverChain_EvenWithMentions()
    {
        var (h, queue, jev) = await ChattyBotsAsync(options: Unlimited(), replyPrefix: "@GPT @Claude ");
        using var disposable = h;
        jev.Unavailable = true;
        var trigger = await h.SayAsync("@GPT @Claude pizza or sushi?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);
        await DrainAsync(h, queue);

        await Assert.That(await BotMessagesAsync(h)).IsEqualTo(2);
    }

    /// <summary>Runs whatever the rounds queued, the way the per-chat worker would.</summary>
    private static async Task<List<int>> DrainAsync(OrchestratorHarness h, BotResponseQueue queue)
    {
        var depths = new List<int>();
        while (queue.Reader.TryRead(out var request))
        {
            depths.Add(request.Depth);
            await h.Orchestrator.ProcessMessageAsync(request.ChatId, request.MessageId, request.Depth);
        }
        return depths;
    }

    private static async Task<int> BotMessagesAsync(OrchestratorHarness h)
    {
        var total = 0;
        foreach (var bot in h.Bots)
            total += (await h.MessagesFromAsync(bot)).Count;
        return total;
    }

    [Test]
    public async Task TwoBots_AnswerEachOther_UpToThreeHops()
    {
        var (h, queue, _) = await ChattyBotsAsync();
        using var disposable = h;
        var trigger = await h.SayAsync("what's the best pizza topping?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);
        var depths = await DrainAsync(h, queue);

        // Both answer the person. Each answer is a message of its own, which
        // the other bot answers, for three hops.
        await Assert.That(depths).IsEquivalentTo([1, 1, 2, 2, 3, 3]);
        await Assert.That(await BotMessagesAsync(h)).IsEqualTo(8);
    }

    [Test]
    public async Task EveryReplyGetsItsOwnDecision_AndTheChainStaysBounded()
    {
        var (h, queue, jev) = await ChattyBotsAsync(botCount: 4);
        using var disposable = h;
        var trigger = await h.SayAsync("what's the best pizza topping?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);
        var depths = await DrainAsync(h, queue);

        await Assert.That(depths.Max()).IsLessThanOrEqualTo(3);
        // One decision for the person's message, and one per bot reply queued.
        await Assert.That(jev.Calls.Count).IsEqualTo(depths.Count + 1);
        // However eager, no bot replies more often than its rate cap allows.
        var max = new BotBehaviorOptions().MaxRepliesPerMinute;
        foreach (var bot in h.Bots)
            await Assert.That((await h.MessagesFromAsync(bot)).Count).IsLessThanOrEqualTo(max);
    }

    [Test]
    public async Task PersonSpeaking_EndsTheChain()
    {
        var (h, queue, _) = await ChattyBotsAsync();
        using var disposable = h;
        var trigger = await h.SayAsync("what's the best pizza topping?");
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        // The person cuts in before the queued bot messages are picked up.
        await h.SayAsync("never mind, ordering sushi");
        await DrainAsync(h, queue);

        await Assert.That(await BotMessagesAsync(h)).IsEqualTo(2);
    }
}

public class BotResponseBackgroundServiceTests
{
    [Test]
    public async Task SlowChat_DoesNotStallOtherChats_AndEachChatStaysInOrder()
    {
        var queue = new BotResponseQueue();
        var orchestrator = Substitute.For<IBotResponseOrchestrator>();
        var (chatA, chatB) = (Guid.NewGuid(), Guid.NewGuid());

        var releaseA = new TaskCompletionSource();
        var firstAStarted = new TaskCompletionSource();
        var secondAStarted = new TaskCompletionSource();
        var aCalls = 0;
        orchestrator.ProcessMessageAsync(chatA, Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var call = Interlocked.Increment(ref aCalls);
                (call == 1 ? firstAStarted : secondAStarted).TrySetResult();
                return releaseA.Task;
            });
        var bDone = new TaskCompletionSource();
        orchestrator.ProcessMessageAsync(chatB, Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                bDone.TrySetResult();
                return Task.CompletedTask;
            });

        var service = new BotResponseBackgroundService(queue, orchestrator, NullLogger<BotResponseBackgroundService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await queue.EnqueueAsync(chatA, Guid.NewGuid());
            await queue.EnqueueAsync(chatA, Guid.NewGuid());
            await queue.EnqueueAsync(chatB, Guid.NewGuid());

            // Chat B is answered while chat A's first message is still stuck...
            await firstAStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await bDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            // ...and chat A's second message waits for its first.
            await Assert.That(Volatile.Read(ref aCalls)).IsEqualTo(1);

            releaseA.SetResult();
            await secondAStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseA.TrySetResult();
            await service.StopAsync(CancellationToken.None);
        }
    }
}
