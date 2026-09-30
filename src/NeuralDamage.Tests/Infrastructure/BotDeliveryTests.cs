using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>How a reply reaches the chat: split, cleaned, paced, and not repeated.</summary>
public class BotDeliveryTests
{
    [Test]
    public async Task ReplyWithBlankLines_SplitsIntoAtMostThreeMessages()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        var gpt = h.Bots[0];
        h.Respond(gpt);
        h.Reply("oh nice\n\n**honestly** that's the best one\n\nthird thought\n\nfourth thought");
        var trigger = await h.SayAsync("I finally watched Alien");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        var sent = await h.MessagesFromAsync(gpt);
        await Assert.That(sent.Select(m => m.Content)).IsEquivalentTo(
            ["oh nice", "honestly that's the best one", "third thought\nfourth thought"]);
        // Only the first part replies; the rest read as the same person carrying on.
        await Assert.That(sent[0].ReplyToId).IsEqualTo(trigger.Id);
        await Assert.That(sent.Skip(1).All(m => m.ReplyToId == null)).IsTrue();
        await h.Notifications.Received(3).NotifyMessageNew(h.Chat.Id, Arg.Any<MessageDto>());
    }

    [Test]
    public async Task NearDuplicateOfARecentMessage_RegeneratesOnce()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        var gpt = h.Bots[0];
        h.Respond(gpt);
        await h.SayAsync("honestly pineapple on pizza is underrated", asBot: gpt, at: DateTime.UtcNow.AddMinutes(-5));
        h.Reply("Honestly, pineapple on pizza is underrated!", "fine, I'll stop talking about pizza");
        var trigger = await h.SayAsync("what do you think about pizza");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        var sent = await h.MessagesFromAsync(gpt);
        await Assert.That(sent[^1].Content).IsEqualTo("fine, I'll stop talking about pizza");
        await h.OpenRouter.ReceivedWithAnyArgs(2).GenerateResponseAsync(default!, default, default!, default!, default);
    }

    // Counts heartbeats against the wall clock, so it must not share the CPU with the
    // rest of the suite: on a 2-core CI runner the parallel image tests starved its timer.
    [Test, NotInParallel]
    public async Task SlowGeneration_KeepsResendingTyping()
    {
        var options = InstantBotOptions.Create();
        options.Value.TypingHeartbeat = TimeSpan.FromMilliseconds(20);
        using var h = await OrchestratorHarness.CreateAsync(options: options);
        var gpt = h.Bots[0];
        h.Respond(gpt);
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(async _ =>
        {
            await Task.Delay(600);
            return "done";
        });
        var trigger = await h.SayAsync("take your time");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        var typing = h.Notifications.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IChatNotificationService.NotifyBotTyping));
        await Assert.That(typing >= 3).IsTrue();
    }

    [Test]
    public async Task CancelledWhileTyping_TellsTheClient()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        var gpt = h.Bots[0];
        h.Respond(gpt);
        var typingStarted = new TaskCompletionSource();
        h.Notifications.NotifyBotTyping(default, default, default!).ReturnsForAnyArgs(_ =>
        {
            typingStarted.TrySetResult();
            return Task.CompletedTask;
        });
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(async ci =>
        {
            await Task.Delay(Timeout.Infinite, ci.ArgAt<CancellationToken>(4));
            return "never";
        });
        var trigger = await h.SayAsync("hello?");

        var round = h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);
        await typingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Orchestrator.CancelPendingResponses(h.Chat.Id);
        await round.WaitAsync(TimeSpan.FromSeconds(5));

        await h.Notifications.Received(1).NotifyBotResponseCancelled(h.Chat.Id);
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifyMessageNew(default, default!);
    }

    [Test]
    public async Task BotMessage_OtherBotsMayReact_ButNeverTheSender()
    {
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var (gpt, claude) = (h.Bots[0], h.Bots[1]);
        h.Respond();

        // Reacting is occasional; across this many runs a reaction is all but certain.
        for (var i = 0; i < 100; i++)
        {
            var message = await h.SayAsync($"that is hilarious {i}", asBot: claude);
            await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);
        }

        var reactions = await h.Db.Reactions.AsNoTracking().ToListAsync();
        await Assert.That(reactions).IsNotEmpty();
        await Assert.That(reactions.All(r => r.BotId == gpt.Id)).IsTrue();
    }

    [Test]
    public async Task NoKeyword_NoReaction()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        h.Respond();

        for (var i = 0; i < 100; i++)
        {
            var message = await h.SayAsync($"my cat died this morning {i}");
            await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);
        }

        await Assert.That(await h.Db.Reactions.AnyAsync()).IsFalse();
    }
}

public class BotReplyFormatterTests
{
    [Test]
    public async Task Split_SingleParagraph_IsOneMessage()
    {
        await Assert.That(BotReplyFormatter.Split("just one line\nand its second line", 3))
            .IsEquivalentTo(["just one line\nand its second line"]);
    }

    [Test]
    public async Task StripMarkdown_RemovesHeadingsBoldAndBullets()
    {
        var text = BotReplyFormatter.StripMarkdown("## Verdict\n- **yes** it is\n* __really__ *stellar*, 2 * 3 * 4");
        await Assert.That(text).IsEqualTo("Verdict\nyes it is\nreally stellar, 2 * 3 * 4");
    }

    [Test]
    [Arguments("romano, obviously\n\n[Alice]: ok got it\n\n[Roger] (→ Alice): parmesan", "romano, obviously")]
    [Arguments("[Giulia] (→ Alice): romano, obviously", "romano, obviously")]
    [Arguments("romano\n[Byte, 2h later]: parmesan", "romano")]
    [Arguments("[1] see my last message: romano", "[1] see my last message: romano")]
    [Arguments("ratio 2:1 [citation needed]", "ratio 2:1 [citation needed]")]
    public async Task DropOtherSpeakers_KeepsOnlyTheBotsOwnTurn(string reply, string expected)
    {
        await Assert.That(BotReplyFormatter.DropOtherSpeakers(reply)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("I think pineapple on pizza is great", true)]
    [Arguments("pineapple on pizza is great, I think!", true)]
    [Arguments("I think anchovies on pizza are terrible", false)]
    [Arguments("lol", false)]
    public async Task IsNearDuplicate(string reply, bool expected)
    {
        await Assert.That(BotReplyFormatter.IsNearDuplicate(reply, ["lol", "I think pineapple on pizza is great"])).IsEqualTo(expected);
    }

    [Test]
    public async Task TypingTime_ScalesWithLength_WithinBounds()
    {
        var options = new BotBehaviorOptions();
        await Assert.That(options.TypingTime(3)).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(options.TypingTime(48)).IsEqualTo(TimeSpan.FromSeconds(4));
        await Assert.That(options.TypingTime(5000)).IsEqualTo(TimeSpan.FromSeconds(8));
    }
}
