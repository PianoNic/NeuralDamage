using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.BackgroundServices;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>Bots answering bots: how far a chain may run, and what stops it.</summary>
public class BotChainTests
{
    /// <summary>
    /// Two bots that always answer each other, so only the depth limit and a
    /// person speaking can end the chain.
    /// </summary>
    private static async Task<(OrchestratorHarness H, BotResponseQueue Queue)> TwoChattyBotsAsync()
    {
        var queue = new BotResponseQueue();
        var h = await OrchestratorHarness.CreateAsync(botCount: 2,
            configure: s => s.AddSingleton<IBotResponseQueue>(queue));
        h.Decisions.DecideRespondersAsync(Arg.Any<Guid>(), Arg.Any<Message>(), Arg.Any<List<Bot>>(), Arg.Any<CancellationToken>())
            .Returns(ci => [h.Bots.First(b => b.Id != ci.ArgAt<Message>(1).SenderBotId).Id]);
        h.Reply("one", "two", "three", "four", "five", "six");
        return (h, queue);
    }

    /// <summary>Runs whatever the round queued, the way the per-chat worker would.</summary>
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

    [Test]
    public async Task BotsAnswerEachOther_UpToThreeHops()
    {
        var (h, queue) = await TwoChattyBotsAsync();
        using var _ = h;
        var trigger = await h.SayAsync("what's the best pizza topping?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);
        var depths = await DrainAsync(h, queue);

        // One reply to the person, then three bot-to-bot hops, then quiet.
        await Assert.That(depths).IsEquivalentTo([1, 2, 3]);
        var botMessages = (await h.MessagesFromAsync(h.Bots[0])).Count + (await h.MessagesFromAsync(h.Bots[1])).Count;
        await Assert.That(botMessages).IsEqualTo(4);
    }

    [Test]
    public async Task PersonSpeaking_EndsTheChain()
    {
        var (h, queue) = await TwoChattyBotsAsync();
        using var _ = h;
        var trigger = await h.SayAsync("what's the best pizza topping?");
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        // The person cuts in before the queued bot message is picked up.
        await h.SayAsync("never mind, ordering sushi");
        await DrainAsync(h, queue);

        var botMessages = (await h.MessagesFromAsync(h.Bots[0])).Count + (await h.MessagesFromAsync(h.Bots[1])).Count;
        await Assert.That(botMessages).IsEqualTo(1);
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
