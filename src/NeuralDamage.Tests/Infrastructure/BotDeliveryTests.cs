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
    [Arguments("(ignores bob completely)")]
    [Arguments("...")]
    [Arguments("…")]
    [Arguments("(sighs)\n\n(leaves the chat)")]
    public async Task StageDirectionOrPunctuationOnly_IsDropped(string reply)
    {
        using var h = await OrchestratorHarness.CreateAsync();
        var gpt = h.Bots[0];
        h.Respond(gpt);
        h.Reply(reply.Replace("\\n", "\n"));
        var trigger = await h.SayAsync("bob says hi");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        await Assert.That(await h.MessagesFromAsync(gpt)).IsEmpty();
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifyMessageNew(default, default!);
        // Dropped quietly: the bot did answer, it just had nothing to say.
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
    }

    [Test]
    public async Task StageDirectionPart_IsDropped_TheRestPosted()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        var gpt = h.Bots[0];
        h.Respond(gpt);
        h.Reply("(rolls eyes)\n\nfine, dune was good\n\n...");
        var trigger = await h.SayAsync("admit it");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        var sent = await h.MessagesFromAsync(gpt);
        await Assert.That(sent.Select(m => m.Content)).IsEquivalentTo(["fine, dune was good"]);
        await Assert.That(sent[0].ReplyToId).IsEqualTo(trigger.Id);
    }

    [Test]
    public async Task ReplyingBots_AllStartTyping_BeforeAnyPosts()
    {
        using var h = await OrchestratorHarness.CreateAsync(botCount: 3);
        h.Respond(h.Bots[0], h.Bots[1], h.Bots[2]);
        var typing = new System.Collections.Concurrent.ConcurrentDictionary<Guid, byte>();
        var allTyping = new TaskCompletionSource();
        h.Notifications.NotifyBotTyping(default, default, default!).ReturnsForAnyArgs(ci =>
        {
            typing[ci.ArgAt<Guid>(1)] = 0;
            if (typing.Count == 3) allTyping.TrySetResult();
            return Task.CompletedTask;
        });
        // Generation only finishes once every bot is typing: run one after
        // another, the first bot would wait forever.
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(async ci =>
        {
            await allTyping.Task.WaitAsync(ci.ArgAt<CancellationToken>(4));
            return $"my take, as {ci.ArgAt<string>(0)}";
        });
        var posted = 0;
        var typersWhenFirstPosted = -1;
        h.Notifications.NotifyMessageNew(default, default!).ReturnsForAnyArgs(_ =>
        {
            if (Interlocked.Increment(ref posted) == 1) typersWhenFirstPosted = typing.Count;
            return Task.CompletedTask;
        });
        var trigger = await h.SayAsync("anyone seen the new dune movie?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(typersWhenFirstPosted).IsEqualTo(3);
        foreach (var bot in h.Bots)
        {
            var sent = await h.MessagesFromAsync(bot);
            await Assert.That(sent).HasSingleItem();
            // Each answers the person's message, not another bot's reply.
            await Assert.That(sent[0].ReplyToId).IsEqualTo(trigger.Id);
        }
    }

    [Test]
    public async Task ParallelReplies_ReadTheHistoryAsTheRoundStarted()
    {
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var (gpt, claude) = (h.Bots[0], h.Bots[1]);
        h.Respond(gpt, claude);
        var gptPosted = new TaskCompletionSource();
        var prompts = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(async ci =>
        {
            var model = ci.ArgAt<string>(0);
            if (model == claude.ModelId)
                await gptPosted.Task.WaitAsync(TimeSpan.FromSeconds(5)); // GPT's reply is already in the chat
            prompts[model] = string.Join("\n", ci.ArgAt<List<ChatMessage>>(3).Select(m => m.Content));
            return model == gpt.ModelId ? "gpt was here first" : "claude, on its own";
        });
        h.Notifications.NotifyMessageNew(default, default!).ReturnsForAnyArgs(ci =>
        {
            if (ci.ArgAt<MessageDto>(1).SenderBotId == gpt.Id) gptPosted.TrySetResult();
            return Task.CompletedTask;
        });
        var trigger = await h.SayAsync("thoughts?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        await Assert.That(prompts[claude.ModelId]).DoesNotContain("gpt was here first");
    }

    [Test]
    public async Task NewMessage_CancelsEveryReplyingBot()
    {
        using var h = await OrchestratorHarness.CreateAsync(botCount: 3);
        h.Respond(h.Bots[0], h.Bots[1], h.Bots[2]);
        var generating = 0;
        var allGenerating = new TaskCompletionSource();
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(async ci =>
        {
            if (Interlocked.Increment(ref generating) == 3) allGenerating.TrySetResult();
            await Task.Delay(Timeout.Infinite, ci.ArgAt<CancellationToken>(4));
            return "never";
        });
        var trigger = await h.SayAsync("hello?");

        var round = h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);
        await allGenerating.Task.WaitAsync(TimeSpan.FromSeconds(5));
        h.Orchestrator.CancelPendingResponses(h.Chat.Id);
        await round.WaitAsync(TimeSpan.FromSeconds(5));

        await h.Notifications.Received(1).NotifyBotResponseCancelled(h.Chat.Id);
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifyMessageNew(default, default!);
        await Assert.That(await h.Db.Messages.CountAsync(m => m.SenderBotId != null)).IsEqualTo(0);
    }

    [Test]
    public async Task OneBotFailing_DoesNotStopTheOthers()
    {
        using var h = await OrchestratorHarness.CreateAsync(botCount: 2);
        var (gpt, claude) = (h.Bots[0], h.Bots[1]);
        h.Respond(gpt, claude);
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
            ci.ArgAt<string>(0) == gpt.ModelId ? throw new HttpRequestException("boom") : Task.FromResult("still here"));
        var trigger = await h.SayAsync("hello?");

        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, trigger.Id);

        await h.Notifications.Received(1).NotifySystemMessage(h.Chat.Id, "GPT failed to respond.");
        await Assert.That((await h.MessagesFromAsync(claude)).Select(m => m.Content)).IsEquivalentTo(["still here"]);
    }
}

public class BotReplyFormatterTests
{
    [Test]
    [Arguments("(ignores bob completely)", true)]
    [Arguments("  (sighs) (leaves)  ", true)]
    [Arguments("...", true)]
    [Arguments("?!", true)]
    [Arguments("…", true)]
    [Arguments("(sighs) fine, whatever", false)]
    [Arguments("😂", false)]
    [Arguments("ok", false)]
    public async Task IsFiller(string part, bool expected)
    {
        await Assert.That(BotReplyFormatter.IsFiller(part)).IsEqualTo(expected);
    }

    [Test]
    public async Task Split_SingleParagraph_IsOneMessage()
    {
        await Assert.That(BotReplyFormatter.Split("just one line\nand its second line", 3))
            .IsEquivalentTo(["just one line\nand its second line"]);
    }

    [Test]
    public async Task Split_CollapsesRunawaySymbolRuns()
    {
        var reply = new string('!', 400) + " zig's a cool one... really?? 😂😂😂😂";
        await Assert.That(BotReplyFormatter.Split(reply, 3))
            .IsEquivalentTo(["!!! zig's a cool one... really?? 😂😂😂😂"]);
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
    [Arguments("Gloomy Gus:\nMushrooms decompose", "Mushrooms decompose")]
    [Arguments("Gloomy Gus: rain again", "rain again")]
    [Arguments("[gloomy gus]: rain again", "rain again")]
    [Arguments("**Gloomy Gus:** rain again", "rain again")]
    [Arguments("**Gloomy Gus**: rain again", "rain again")]
    [Arguments("Gloomy Gustav: rain again", "Gloomy Gustav: rain again")]
    [Arguments("rain again, Gloomy Gus: sad", "rain again, Gloomy Gus: sad")]
    [Arguments("Gloomy Gus:\nRust or Go\n\nGloomy Gus:\nRain again", "Rust or Go\n\nRain again")]
    public async Task StripOwnName_DropsTheBotsNameOpeningTheReply(string reply, string expected)
    {
        await Assert.That(BotReplyFormatter.StripOwnName(reply, "Gloomy Gus")).IsEqualTo(expected);
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
