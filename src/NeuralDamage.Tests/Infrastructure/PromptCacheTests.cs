using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>
/// Provider prompt caches only hit when a request starts the same way as an
/// earlier one, so a bot's prompt has to keep its beginning from call to call.
/// </summary>
public class PromptCacheTests
{
    private static readonly User Alice = new() { ExternalId = "e", Email = "a@b.com", DisplayName = "Alice" };

    private static Message Said(int i, DateTime start) => new()
    {
        ChatId = Guid.Empty, SenderUserId = Alice.Id, SenderUser = Alice,
        Content = $"msg{i:D3} " + new string('a', 300), CreatedAt = start.AddMinutes(i),
    };

    [Test]
    public async Task TwoRepliesInALongChat_ShareTheSystemPromptAndHistoryStart()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        var gpt = h.Bots[0];
        h.Respond(gpt);
        var calls = new List<(string System, List<ChatMessage> History)>();
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            calls.Add((ci.ArgAt<string>(2), ci.ArgAt<List<ChatMessage>>(3)));
            return Task.FromResult($"reply {calls.Count}");
        });

        // Far more than fits in the history budget.
        var start = DateTime.UtcNow.AddHours(-5);
        for (var i = 0; i < 120; i++)
            await h.SayAsync($"msg{i:D3} " + new string('a', 300), at: start.AddMinutes(i));

        var first = await h.SayAsync("what do you think?", at: start.AddMinutes(200));
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, first.Id);
        var second = await h.SayAsync("and now?", at: DateTime.UtcNow.AddMinutes(1));
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, second.Id);

        await Assert.That(calls.Count).IsEqualTo(2);
        await Assert.That(calls[1].System).IsEqualTo(calls[0].System);
        await Assert.That(calls[0].System).DoesNotContain("local time");

        // The time goes in a note after the history, not in the cached part.
        await Assert.That(calls[0].History[^1].Role).IsEqualTo(ChatMessage.Note);
        await Assert.That(calls[0].History[^1].Content).Contains("local time");

        // Everything before the first call's trigger (which loses its
        // "answering this" mark) is sent again unchanged, in the same order.
        var prefix = calls[0].History[..^2];
        await Assert.That(prefix.Count > 10).IsTrue();
        await Assert.That(calls[1].History.Take(prefix.Count)).IsEquivalentTo(prefix, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(calls[1].History[^2].Content).Contains("and now?");
    }

    [Test]
    public async Task OverBudget_DropsAQuarterAtOnce_LeavingRoomToGrow()
    {
        var start = DateTime.UtcNow.AddDays(-1);
        var messages = Enumerable.Range(0, 60).Select(i => Said(i, start)).ToList();

        var window = BotPromptBuilder.TrimHistory(messages);

        // Well under the budget, not just under it, and still the newest ones.
        var chars = BotPromptBuilder.BuildHistory(window, Guid.NewGuid()).Sum(m => m.Content.Length);
        await Assert.That(chars <= 12_000 * 0.75).IsTrue();
        await Assert.That(window[^1]).IsEqualTo(messages[^1]);
        await Assert.That(window).IsEquivalentTo(messages[^window.Count..], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task WindowStart_StaysPutForManyTurns_ThenStepsAQuarter()
    {
        var start = DateTime.UtcNow.AddDays(-1);
        var chat = Enumerable.Range(0, 40).Select(i => Said(i, start)).ToList();
        var window = BotPromptBuilder.TrimHistory(chat);

        // Replay the orchestrator: each turn loads from where the last window
        // started, plus the new message.
        var starts = new List<Guid> { window[0].Id };
        var sizes = new List<int> { window.Count };
        for (var i = 40; i < 100; i++)
        {
            chat.Add(Said(i, start));
            var loaded = chat.Where(m => m.CreatedAt >= window[0].CreatedAt).ToList();
            window = BotPromptBuilder.TrimHistory(loaded);
            starts.Add(window[0].Id);
            sizes.Add(window.Count);
        }

        // The start moved only a few times in 60 turns, and each move dropped
        // at least a quarter of the window at once.
        var moves = Enumerable.Range(1, starts.Count - 1).Where(i => starts[i] != starts[i - 1]).ToList();
        await Assert.That(moves.Count is > 0 and <= 10).IsTrue();
        foreach (var i in moves)
            await Assert.That(sizes[i] <= (sizes[i - 1] + 1) * 3 / 4).IsTrue();
    }

    [Test]
    public async Task Trimming_KeepsTheTrigger()
    {
        var start = DateTime.UtcNow.AddDays(-1);
        var messages = Enumerable.Range(0, 80).Select(i => Said(i, start)).ToList();
        var trigger = messages[10];

        var window = BotPromptBuilder.TrimHistory(messages, trigger.Id);

        await Assert.That(window).Contains(trigger);
        await Assert.That(window[^1]).IsEqualTo(messages[^1]);
    }

    [Test]
    public async Task CacheBreakpoint_GoesOnTheLastTurnBeforeTheNote()
    {
        List<ChatMessage> history = [new("user", "[Alice]: hi"), new("assistant", "yo"), BotPromptBuilder.BuildNote()];

        var json = OpenRouterAgentService.MessagesWithCacheBreakpoint("system", history, 1).ToString();

        await Assert.That(json).StartsWith("""[{"role":"system","content":"system"},{"role":"user","content":"[Alice]: hi"},""");
        await Assert.That(json).Contains("""{"role":"assistant","content":[{"type":"text","text":"yo","cache_control":{"type":"ephemeral"}}]}""");
        await Assert.That(json).EndsWith("""{"role":"user","content":"(It is """ + history[2].Content[7..] + "\"}]");
    }

    [Test]
    public async Task FirstMessageInTheWindow_NeverNotesAGap()
    {
        var start = DateTime.UtcNow.AddDays(-1);
        var earlier = Said(0, start);
        var later = Said(0, start.AddHours(3));

        var history = BotPromptBuilder.BuildHistory([earlier, later], Guid.NewGuid());
        var alone = BotPromptBuilder.BuildHistory([later], Guid.NewGuid());

        await Assert.That(history[1].Content).Contains("3h later");
        await Assert.That(alone[0].Content).DoesNotContain("later");
    }
}
