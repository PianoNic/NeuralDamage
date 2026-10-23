using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>
/// Long chats: the raw history follows the model's context, and what drops
/// out of it is folded into a running summary at the start of the history.
/// </summary>
public class ChatCompactionTests
{
    // ---- Context length ----

    private const string Refusal = """
        {"error":{"type":"BadRequestError","code":"400","message":"This model's maximum context length is 131072 tokens. However, you requested 16 output tokens and your prompt contains at least 131057 input tokens."}}
        """;

    private const string Completion = """
        {"id":"c","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"h"},"finish_reason":"length"}],"usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
        """;

    /// <summary>Answers models lists by URL, and every completion with <paramref name="completion"/>.</summary>
    private sealed class FakeServers(Dictionary<string, string> modelLists, HttpStatusCode completionStatus, string completion) : HttpMessageHandler
    {
        public List<(string Url, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Requests)
                Requests.Add((url, body?.Length > 1000 ? body[..1000] : body));
            if (url.EndsWith("/chat/completions", StringComparison.Ordinal))
                return new HttpResponseMessage(completionStatus) { Content = new StringContent(completion, Encoding.UTF8, "application/json") };
            return modelLists.TryGetValue(url, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.BadGateway);
        }
    }

    /// <summary>Unique hosts per test: the catalogue and the probe results are cached by URL.</summary>
    private static (OpenRouterAgentService Service, FakeServers Servers) Service(
        string host, Dictionary<string, string?> settings, Dictionary<string, string> modelLists,
        HttpStatusCode completionStatus = HttpStatusCode.BadRequest, string completion = Refusal)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(settings)
        {
            ["OpenRouter:ApiKey"] = "or-key",
            ["OpenRouter:BaseUrl"] = $"http://or-{host}.test/api/v1",
            ["OpenRouter:RetryDelay"] = "00:00:00.001",
        }).Build();
        modelLists.TryAdd($"http://or-{host}.test/api/v1/models", """{"data":[]}""");
        var servers = new FakeServers(modelLists, completionStatus, completion);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(servers, disposeHandler: false));
        return (new OpenRouterAgentService(configuration, factory, new ModelPolicy(0, 0), NullLogger<OpenRouterAgentService>.Instance), servers);
    }

    private static int Probes(FakeServers servers) =>
        servers.Requests.Count(r => r.Url.EndsWith("/chat/completions", StringComparison.Ordinal));

    [Test]
    public async Task ContextLength_ConfiguredWins_OverModelsList()
    {
        var (service, servers) = Service("cfg", new()
        {
            ["Providers:gw:BaseUrl"] = "http://gw-cfg.test/v1",
            ["Providers:gw:ContextLength"] = "8000",
        }, new() { ["http://gw-cfg.test/v1/models"] = """{"data":[{"id":"m","context_length":64000}]}""" });

        await Assert.That(await service.ContextLengthAsync("gw:m")).IsEqualTo(8000);
        await Assert.That(Probes(servers)).IsEqualTo(0);
    }

    [Test]
    [Arguments("context_length")]
    [Arguments("max_model_len")]
    public async Task ContextLength_ComesFromModelsList(string field)
    {
        var host = field.Replace('_', '-');
        var (service, servers) = Service(host, new() { ["Providers:gw:BaseUrl"] = $"http://gw-{host}.test/v1" },
            new() { [$"http://gw-{host}.test/v1/models"] = $$"""{"data":[{"id":"m","{{field}}":65536}]}""" });

        await Assert.That(await service.ContextLengthAsync("gw:m")).IsEqualTo(65536);
        await Assert.That(Probes(servers)).IsEqualTo(0);
    }

    [Test]
    public async Task ContextLength_OfAnOpenRouterModel_IsFromTheCatalogue_OrTheFallback()
    {
        var (service, servers) = Service("ormodel", [], new()
        {
            ["http://or-ormodel.test/api/v1/models"] = """{"data":[{"id":"deepseek/deepseek-chat","name":"V3","context_length":64000}]}""",
        });

        await Assert.That(await service.ContextLengthAsync("deepseek/deepseek-chat")).IsEqualTo(64000);
        await Assert.That(await service.ContextLengthAsync("vendor/unknown")).IsEqualTo(OpenRouterAgentService.FallbackContextLength);
        await Assert.That(Probes(servers)).IsEqualTo(0);
    }

    [Test]
    public async Task ContextLength_WithoutConfigOrList_IsProbedOnce_FromTheRefusal()
    {
        var (service, servers) = Service("probe", new()
        {
            ["Providers:gw:BaseUrl"] = "http://gw-probe.test/v1",
            ["Providers:gw:ApiKey"] = "gw-key",
            ["Providers:gw:Models"] = "deepseek-chat",
        }, []);

        await Assert.That(await service.ContextLengthAsync("gw:deepseek-chat")).IsEqualTo(131072);
        await Assert.That(await service.ContextLengthAsync("gw:deepseek-chat")).IsEqualTo(131072);

        await Assert.That(Probes(servers)).IsEqualTo(1);
        var probe = servers.Requests.Single(r => r.Url.EndsWith("/chat/completions", StringComparison.Ordinal));
        await Assert.That(probe.Url).IsEqualTo("http://gw-probe.test/v1/chat/completions");
        await Assert.That(probe.Body!).Contains("\"max_tokens\":1");
        await Assert.That(probe.Body!).Contains("\"model\":\"deepseek-chat\"");
    }

    [Test]
    [Arguments(HttpStatusCode.OK, Completion)]
    [Arguments(HttpStatusCode.BadRequest, """{"error":{"message":"prompt too long"}}""")]
    public async Task ContextLength_ProbeAcceptedOrUnreadable_FallsBack_AndIsNotRepeated(HttpStatusCode status, string body)
    {
        var host = $"fallback{(int)status}";
        var (service, servers) = Service(host, new()
        {
            ["Providers:gw:BaseUrl"] = $"http://gw-{host}.test/v1",
            ["Providers:gw:Models"] = "m",
        }, [], status, body);

        await Assert.That(await service.ContextLengthAsync("gw:m")).IsEqualTo(OpenRouterAgentService.FallbackContextLength);
        await Assert.That(await service.ContextLengthAsync("gw:m")).IsEqualTo(OpenRouterAgentService.FallbackContextLength);
        await Assert.That(Probes(servers)).IsEqualTo(1);
    }

    [Test]
    public async Task ParseContextLength_ReadsTheLimit()
    {
        await Assert.That(OpenRouterAgentService.ParseContextLength(Refusal)).IsEqualTo(131072);
        await Assert.That(OpenRouterAgentService.ParseContextLength("This model's Maximum Context Length is 8192 tokens")).IsEqualTo(8192);
        await Assert.That(OpenRouterAgentService.ParseContextLength("context window exceeded")).IsNull();
        await Assert.That(OpenRouterAgentService.ParseContextLength(null)).IsNull();
    }

    // ---- Budget ----

    [Test]
    [Arguments(null, 12_000)]
    [Arguments(0, 12_000)]
    [Arguments(8_192, 12_000)]
    [Arguments(64_000, 64_000)]
    [Arguments(131_072, 128_000)]
    [Arguments(1_000_000, 128_000)]
    public async Task Budget_IsAShareOfTheContext_CappedAndNeverBelowTheOldBudget(int? context, int chars)
    {
        var budget = HistoryBudget.For(context, new HistoryOptions());

        await Assert.That(budget.Chars).IsEqualTo(chars);
        await Assert.That(budget.Messages >= HistoryBudget.Default.Messages).IsTrue();
    }

    [Test]
    public async Task Budget_FollowsConfiguration()
    {
        var budget = HistoryBudget.For(100_000, new HistoryOptions { ContextShare = 0.5, MaxTokens = 10_000, MinChars = 1_000 });

        await Assert.That(budget.Chars).IsEqualTo(40_000);
        await Assert.That(HistoryBudget.Default.Messages).IsEqualTo(50);
        await Assert.That(HistoryBudget.For(null, new HistoryOptions { MinChars = 2_400 }).Messages).IsEqualTo(10);
    }

    [Test]
    public async Task BiggerBudget_KeepsMoreHistory()
    {
        var start = DateTime.UtcNow.AddDays(-1);
        var messages = Enumerable.Range(0, 200).Select(i => Said(i, start)).ToList();

        var small = BotPromptBuilder.TrimHistory(messages);
        var big = BotPromptBuilder.TrimHistory(messages, budget: HistoryBudget.For(131_072, new HistoryOptions()));

        await Assert.That(big.Count).IsEqualTo(200);
        await Assert.That(small.Count < 50).IsTrue();
    }

    // ---- Placement and cache stability ----

    private static readonly User Alice = new() { ExternalId = "e", Email = "a@b.com", DisplayName = "Alice" };

    private static Message Said(int i, DateTime start) => new()
    {
        ChatId = Guid.Empty, SenderUserId = Alice.Id, SenderUser = Alice,
        Content = $"msg{i:D3} " + new string('a', 300), CreatedAt = start.AddMinutes(i),
    };

    [Test]
    public async Task Summary_GoesFirst_AsAUserTurn()
    {
        var start = DateTime.UtcNow.AddDays(-1);
        List<Message> messages = [Said(0, start), Said(1, start)];

        var history = BotPromptBuilder.BuildHistory(messages, Guid.NewGuid(), summary: "Alice has a cat called Mochi.");
        var without = BotPromptBuilder.BuildHistory(messages, Guid.NewGuid());

        await Assert.That(history.Count).IsEqualTo(3);
        await Assert.That(history[0]).IsEqualTo(new ChatMessage("user", "(Earlier in this chat: Alice has a cat called Mochi.)"));
        await Assert.That(history.Skip(1)).IsEquivalentTo(without, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(BotPromptBuilder.BuildSystemPrompt(new Bot { Name = "Rex", ModelId = "m", CreatedById = Guid.Empty }, ["Alice", "Rex"]))
            .Contains("never mention it");
    }

    /// <summary>A chat far past the budget, with a stored summary, answered twice by one bot.</summary>
    private static async Task<(OrchestratorHarness H, List<List<ChatMessage>> Calls)> LongChatAsync(string? summary, DateTime? summarizedBefore, IChatCompactor? compactor = null)
    {
        var h = await OrchestratorHarness.CreateAsync(configure: compactor is null ? null : s => s.AddSingleton(compactor));
        h.Respond(h.Bots[0]);
        var calls = new List<List<ChatMessage>>();
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            calls.Add(ci.ArgAt<List<ChatMessage>>(3));
            return Task.FromResult($"reply {calls.Count}");
        });

        var chat = await h.Db.Chats.SingleAsync(c => c.Id == h.Chat.Id);
        chat.Summary = summary;
        chat.SummarizedBefore = summarizedBefore;
        var start = DateTime.UtcNow.AddHours(-5);
        for (var i = 0; i < 120; i++)
            await h.SayAsync($"msg{i:D3} " + new string('a', 300), at: start.AddMinutes(i));
        return (h, calls);
    }

    private static async Task SetSummaryAsync(OrchestratorHarness h, string summary, DateTime before)
    {
        var chat = await h.Db.Chats.SingleAsync(c => c.Id == h.Chat.Id);
        chat.Summary = summary;
        chat.SummarizedBefore = before;
        await h.Db.SaveChangesAsync();
    }

    [Test]
    public async Task StartOfTheHistory_IsUnchangedBetweenTrims_EvenWhenAnotherBotCompacts()
    {
        var (h, calls) = await LongChatAsync(null, null);
        using var _ = h;
        // The summary ends at msg100, inside the window.
        await SetSummaryAsync(h, "Alice has a cat called Mochi.", (await h.Db.Messages.SingleAsync(m => m.Content.StartsWith("msg100"))).CreatedAt);

        var first = await h.SayAsync("what do you think?", at: DateTime.UtcNow);
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, first.Id);
        // Another bot's window dropped a chunk and the summary moved on.
        await SetSummaryAsync(h, "Alice has a cat called Mochi. Bob moved to Bern.", DateTime.UtcNow);
        var second = await h.SayAsync("and now?", at: DateTime.UtcNow.AddMinutes(1));
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, second.Id);

        await Assert.That(calls.Count).IsEqualTo(2);
        await Assert.That(calls[0][0].Content).IsEqualTo("(Earlier in this chat: Alice has a cat called Mochi.)");
        var prefix = calls[0][..^2];
        await Assert.That(prefix.Count > 10).IsTrue();
        await Assert.That(calls[1].Take(prefix.Count)).IsEquivalentTo(prefix, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task NewSummary_IsTakenUp_OnceItReachesTheWindow()
    {
        var (h, calls) = await LongChatAsync(null, null);
        using var _ = h;

        var first = await h.SayAsync("what do you think?", at: DateTime.UtcNow);
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, first.Id);
        // The compaction this trim asked for has finished.
        await SetSummaryAsync(h, "Alice has a cat called Mochi.", h.BotState.HistoryStart(h.Chat.Id, h.Bots[0].Id)!.Value);
        var second = await h.SayAsync("and now?", at: DateTime.UtcNow.AddMinutes(1));
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, second.Id);

        await Assert.That(calls[0][0].Content).StartsWith("[Alice]");
        await Assert.That(calls[1][0].Content).IsEqualTo("(Earlier in this chat: Alice has a cat called Mochi.)");
    }

    // ---- Trigger ----

    [Test]
    public async Task Trim_SchedulesCompaction_OfEverythingBeforeTheWindow()
    {
        var compactor = Substitute.For<IChatCompactor>();
        var (h, _) = await LongChatAsync(null, null, compactor);
        using var _h = h;

        var message = await h.SayAsync("what do you think?", at: DateTime.UtcNow);
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        var windowStart = h.BotState.HistoryStart(h.Chat.Id, h.Bots[0].Id)!.Value;
        await compactor.Received(1).Schedule(h.Chat.Id, windowStart);
    }

    [Test]
    public async Task NoTrim_SchedulesNothing()
    {
        var compactor = Substitute.For<IChatCompactor>();
        using var h = await OrchestratorHarness.CreateAsync(configure: s => s.AddSingleton(compactor));
        h.Respond(h.Bots[0]);
        h.Reply("sure");

        var message = await h.SayAsync("hi");
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        await compactor.DidNotReceiveWithAnyArgs().Schedule(default, default);
    }

    // ---- Compaction ----

    private static async Task<(OrchestratorHarness H, ChatCompactor Compactor, DateTime Before)> CompactorAsync(HistoryOptions? options = null)
    {
        var h = await OrchestratorHarness.CreateAsync();
        var start = DateTime.UtcNow.AddHours(-2);
        var chat = await h.Db.Chats.SingleAsync(c => c.Id == h.Chat.Id);
        chat.Summary = "Alice has a cat called Mochi.";
        chat.SummarizedBefore = start;
        await h.Db.SaveChangesAsync();
        await h.SayAsync("already summarised", at: start.AddMinutes(-1));
        await h.SayAsync("I just moved to Bern", at: start.AddMinutes(1));
        await h.SayAsync("my nickname is Ali", at: start.AddMinutes(2));
        await h.SayAsync("still in the window", at: start.AddMinutes(10));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:DefaultModel"] = "gw:deepseek-chat" }).Build();
        var compactor = new ChatCompactor(h.Provider.GetRequiredService<IServiceScopeFactory>(), configuration,
            NullLogger<ChatCompactor>.Instance, Microsoft.Extensions.Options.Options.Create(options ?? new HistoryOptions()));
        return (h, compactor, start.AddMinutes(10));
    }

    private static Task<Chat> StoredAsync(OrchestratorHarness h) =>
        h.Db.Chats.AsNoTracking().SingleAsync(c => c.Id == h.Chat.Id);

    [Test]
    public async Task Compaction_FoldsTheDroppedMessages_WithTheDefaultModel()
    {
        var (h, compactor, before) = await CompactorAsync();
        using var _ = h;
        h.Reply("Alice has a cat called Mochi, moved to Bern and goes by Ali.");

        await compactor.Schedule(h.Chat.Id, before);

        var chat = await StoredAsync(h);
        await Assert.That(chat.Summary).IsEqualTo("Alice has a cat called Mochi, moved to Bern and goes by Ali.");
        await Assert.That(chat.SummarizedBefore).IsEqualTo(before);
        var call = h.OpenRouter.ReceivedCalls().Single().GetArguments();
        await Assert.That((string)call[0]!).IsEqualTo("gw:deepseek-chat");
        var input = ((List<ChatMessage>)call[3]!).Single().Content;
        await Assert.That(input).Contains("Alice has a cat called Mochi.");
        await Assert.That(input).Contains("[Alice]: I just moved to Bern");
        await Assert.That(input).DoesNotContain("already summarised");
        await Assert.That(input).DoesNotContain("still in the window");
    }

    [Test]
    public async Task Compaction_Failure_KeepsTheOldSummary()
    {
        var (h, compactor, before) = await CompactorAsync();
        using var _ = h;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ThrowsAsyncForAnyArgs(new HttpRequestException("down"));

        await compactor.Schedule(h.Chat.Id, before);
        await Assert.That(async () => await compactor.CompactAsync(h.Chat.Id, before)).Throws<HttpRequestException>();

        var chat = await StoredAsync(h);
        await Assert.That(chat.Summary).IsEqualTo("Alice has a cat called Mochi.");
        await Assert.That(chat.SummarizedBefore).IsEqualTo(before.AddMinutes(-10));
    }

    [Test]
    public async Task Compaction_EmptyAnswer_KeepsTheOldSummary()
    {
        var (h, compactor, before) = await CompactorAsync();
        using var _ = h;
        h.Reply("  ");

        await compactor.Schedule(h.Chat.Id, before);

        await Assert.That((await StoredAsync(h)).Summary).IsEqualTo("Alice has a cat called Mochi.");
    }

    [Test]
    public async Task Compaction_TooLong_IsCompactedAgain_AndCapped()
    {
        var (h, compactor, before) = await CompactorAsync(new HistoryOptions { SummaryMaxChars = 300 });
        using var _ = h;
        h.Reply(new string('x', 900) + ".", "Short. " + new string('y', 400));

        await compactor.Schedule(h.Chat.Id, before);

        var summary = (await StoredAsync(h)).Summary!;
        await Assert.That(h.OpenRouter.ReceivedCalls().Count()).IsEqualTo(2);
        await Assert.That(summary.Length <= 300).IsTrue();
        await Assert.That(summary).StartsWith("Short.");
    }

    [Test]
    public async Task Compaction_RunsOncePerChatAtATime()
    {
        var (h, compactor, before) = await CompactorAsync();
        using var _ = h;
        var gate = new TaskCompletionSource<string>();
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(gate.Task);

        var running = compactor.Schedule(h.Chat.Id, before);
        var second = compactor.Schedule(h.Chat.Id, before);
        await Assert.That(second.IsCompleted).IsTrue();
        gate.SetResult("Alice moved to Bern.");
        await running;

        await Assert.That(h.OpenRouter.ReceivedCalls().Count()).IsEqualTo(1);
        await Assert.That((await StoredAsync(h)).Summary).IsEqualTo("Alice moved to Bern.");
    }

    // ---- /compact ----

    private static ChatCompactor NewCompactor(OrchestratorHarness h, HistoryOptions? options = null) =>
        new(h.Provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:DefaultModel"] = "gw:deepseek-chat" }).Build(),
            NullLogger<ChatCompactor>.Instance, Microsoft.Extensions.Options.Options.Create(options ?? new HistoryOptions()));

    [Test]
    public async Task CompactNow_FoldsAllButTheLastFew_AndMovesTheSummaryOn()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        var start = DateTime.UtcNow.AddHours(-2);
        var messages = new List<Message>();
        for (var i = 0; i < 8; i++)
            messages.Add(await h.SayAsync($"msg{i}", at: start.AddMinutes(i)));
        h.Reply("Alice said eight things.");

        var folded = await NewCompactor(h, new HistoryOptions { CompactKeep = 3 }).CompactNowAsync(h.Chat.Id);

        await Assert.That(folded).IsEqualTo(5);
        var chat = await StoredAsync(h);
        await Assert.That(chat.Summary).IsEqualTo("Alice said eight things.");
        await Assert.That(chat.SummarizedBefore).IsEqualTo(messages[5].CreatedAt);
        var input = ((List<ChatMessage>)h.OpenRouter.ReceivedCalls().Single().GetArguments()[3]!).Single().Content;
        await Assert.That(input).Contains("[Alice]: msg4");
        await Assert.That(input).DoesNotContain("msg5");
    }

    [Test]
    public async Task CompactNow_OnlyFoldsWhatIsNotSummarisedYet()
    {
        var (h, _, before) = await CompactorAsync();
        using var _h = h;
        h.Reply("Alice has a cat called Mochi and lives in Bern.");

        // Three messages after the summary; keeping one folds two.
        var folded = await NewCompactor(h, new HistoryOptions { CompactKeep = 1 }).CompactNowAsync(h.Chat.Id);

        await Assert.That(folded).IsEqualTo(2);
        await Assert.That((await StoredAsync(h)).SummarizedBefore).IsEqualTo(before);
    }

    [Test]
    public async Task CompactNow_NothingToFold_CallsNoModel()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        for (var i = 0; i < 10; i++)
            await h.SayAsync($"msg{i}", at: DateTime.UtcNow.AddMinutes(i - 20));

        var folded = await NewCompactor(h).CompactNowAsync(h.Chat.Id);

        await Assert.That(folded).IsEqualTo(0);
        await Assert.That(h.OpenRouter.ReceivedCalls()).IsEmpty();
        await Assert.That((await StoredAsync(h)).SummarizedBefore).IsNull();
    }

    [Test]
    public async Task CompactNow_WaitsForNoOne_WhileTheChatIsBeingCompacted()
    {
        var (h, compactor, before) = await CompactorAsync(new HistoryOptions { CompactKeep = 1 });
        using var _ = h;
        var gate = new TaskCompletionSource<string>();
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(gate.Task);

        var running = compactor.Schedule(h.Chat.Id, before);
        var now = await compactor.CompactNowAsync(h.Chat.Id);
        gate.SetResult("Alice moved to Bern.");
        await running;

        await Assert.That(now).IsNull();
        await Assert.That(h.OpenRouter.ReceivedCalls().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task CompactNow_ModelFails_Throws_AndChangesNothing()
    {
        var (h, compactor, before) = await CompactorAsync(new HistoryOptions { CompactKeep = 1 });
        using var _ = h;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ThrowsAsyncForAnyArgs(new HttpRequestException("down"));

        await Assert.That(async () => await compactor.CompactNowAsync(h.Chat.Id)).Throws<HttpRequestException>();

        await Assert.That((await StoredAsync(h)).SummarizedBefore).IsEqualTo(before.AddMinutes(-10));
        // The lock is let go: the next try runs.
        h.Reply("Alice moved to Bern.");
        await Assert.That(await compactor.CompactNowAsync(h.Chat.Id)).IsEqualTo(2);
    }

    [Test]
    public async Task AfterCompactNow_TheNextPromptStartsWhereTheSummaryEnds()
    {
        using var h = await OrchestratorHarness.CreateAsync();
        h.Respond(h.Bots[0]);
        var calls = new List<List<ChatMessage>>();
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            var history = ci.ArgAt<List<ChatMessage>>(3);
            calls.Add(history);
            // The summary model gets one turn; a bot gets the history and the note.
            return Task.FromResult(history.Count == 1 ? "Alice said a lot of things." : $"reply {calls.Count}");
        });
        var start = DateTime.UtcNow.AddHours(-3);
        for (var i = 0; i < 30; i++)
            await h.SayAsync($"msg{i:D2}", at: start.AddMinutes(i));

        var first = await h.SayAsync("what do you think?", at: start.AddMinutes(31));
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, first.Id);
        var before = calls[^1];

        var folded = await NewCompactor(h).CompactNowAsync(h.Chat.Id);
        h.BotState.ForgetHistory(h.Chat.Id);
        var second = await h.SayAsync("and now?", at: DateTime.UtcNow.AddSeconds(1));
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, second.Id);
        var after = calls[^1];

        var summarizedBefore = (await StoredAsync(h)).SummarizedBefore!.Value;
        var oldestKept = await h.Db.Messages.AsNoTracking().SingleAsync(m => m.CreatedAt == summarizedBefore);
        await Assert.That(folded > 0).IsTrue();
        await Assert.That(after[0].Content).IsEqualTo("(Earlier in this chat: Alice said a lot of things.)");
        await Assert.That(after[1].Content).Contains(oldestKept.Content);
        await Assert.That(after.Any(m => m.Content.Contains("msg00"))).IsFalse();
        await Assert.That(after.Count < before.Count).IsTrue();
    }

    [Test]
    public async Task FreshHistory_StartsNoEarlierThanTheSummary()
    {
        var (h, calls) = await LongChatAsync("Alice has a cat called Mochi.", null);
        using var _ = h;
        var cut = await h.Db.Messages.AsNoTracking().OrderByDescending(m => m.CreatedAt).Skip(5).Select(m => m.CreatedAt).FirstAsync();
        await SetSummaryAsync(h, "Alice has a cat called Mochi.", cut);

        var message = await h.SayAsync("what do you think?", at: DateTime.UtcNow);
        await h.Orchestrator.ProcessMessageAsync(h.Chat.Id, message.Id);

        // The summary, the six messages from where it ends, this one and the note.
        await Assert.That(calls.Single().Count).IsEqualTo(1 + 6 + 1 + 1);
        await Assert.That(h.BotState.HistoryStart(h.Chat.Id, h.Bots[0].Id)).IsEqualTo(cut);
    }

    // ---- /summarize ----

    [Test]
    public async Task Summarize_SendsTheRecapToTheCallerOnly_AndStoresNothing()
    {
        var (h, compactor, before) = await CompactorAsync();
        using var _ = h;
        h.Reply("Alice has a cat, moved to Bern and goes by Ali.");
        var messages = await h.Db.Messages.CountAsync();

        await compactor.Summarize(h.Chat.Id, h.User.Id);

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.Chat.Id, h.User.Id, "Summary of the chat so far:\nAlice has a cat, moved to Bern and goes by Ali.");
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
        var call = h.OpenRouter.ReceivedCalls().Single().GetArguments();
        await Assert.That((string)call[0]!).IsEqualTo("gw:deepseek-chat");
        var input = ((List<ChatMessage>)call[3]!).Single().Content;
        await Assert.That(input).Contains("Alice has a cat called Mochi.");
        await Assert.That(input).Contains("[Alice]: still in the window");
        await Assert.That(input).DoesNotContain("already summarised");
        var chat = await StoredAsync(h);
        await Assert.That(chat.Summary).IsEqualTo("Alice has a cat called Mochi.");
        await Assert.That(chat.SummarizedBefore).IsEqualTo(before.AddMinutes(-10));
        await Assert.That(await h.Db.Messages.CountAsync()).IsEqualTo(messages);
    }

    [Test]
    public async Task Summarize_ModelFails_TellsTheCaller()
    {
        var (h, compactor, _) = await CompactorAsync();
        using var _h = h;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ThrowsAsyncForAnyArgs(new HttpRequestException("down"));

        await compactor.Summarize(h.Chat.Id, h.User.Id);

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.Chat.Id, h.User.Id, Arg.Is<string>(s => s.StartsWith("Couldn't summarize the chat")));
    }

    [Test]
    public async Task Summarize_EmptyChat_SaysSo_WithoutAModel()
    {
        using var h = await OrchestratorHarness.CreateAsync();

        await NewCompactor(h).Summarize(h.Chat.Id, h.User.Id);

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.Chat.Id, h.User.Id, "Nothing to summarize yet.");
        await Assert.That(h.OpenRouter.ReceivedCalls()).IsEmpty();
    }

    [Test]
    public async Task Compaction_AfterTheChatWasCleared_WritesNothing()
    {
        var (h, compactor, before) = await CompactorAsync();
        using var _ = h;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(async _ =>
        {
            // What ClearChatHandler does, while the model is writing.
            using var scope = h.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamage.Infrastructure.NeuralDamageDbContext>();
            db.Messages.RemoveRange(db.Messages);
            var chat = await db.Chats.SingleAsync(c => c.Id == h.Chat.Id);
            chat.Summary = null;
            chat.SummarizedBefore = null;
            await db.SaveChangesAsync();
            return "stale";
        });

        await compactor.Schedule(h.Chat.Id, before);

        await Assert.That((await StoredAsync(h)).Summary).IsNull();
    }
}
