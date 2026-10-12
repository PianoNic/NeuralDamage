using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NeuralDamage.Application.Queries;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>
/// Models from OpenAI-compatible endpoints next to OpenRouter
/// (<c>Providers:&lt;Name&gt;</c>), addressed as <c>&lt;Name&gt;:&lt;model&gt;</c>.
/// </summary>
public class CustomProviderTests
{
    private const string Completion = """
        {"id":"c","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
        """;

    private const string OpenRouterModels = """
        {"data":[{"id":"deepseek/deepseek-chat","name":"DeepSeek: V3","context_length":64000,"pricing":{"prompt":"0.0000002","completion":"0.0000008"},"supported_parameters":["reasoning"]}]}
        """;

    private sealed record Seen(string Url, string? Authorization, string? Body);

    /// <summary>Answers by URL: a chat completion, a models list, or a failure for a host that is "down".</summary>
    private sealed class FakeServers(Dictionary<string, string> modelLists) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Requests)
                Requests.Add(new Seen(url, request.Headers.Authorization?.ToString(), body));
            if (url.EndsWith("/chat/completions", StringComparison.Ordinal))
                return Json(Completion);
            return modelLists.TryGetValue(url, out var json) ? Json(json) : new HttpResponseMessage(HttpStatusCode.BadGateway);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    /// <summary>Unique hosts per test, since the catalogue cache is shared by URL.</summary>
    private static (OpenRouterAgentService Service, FakeServers Servers, ListLogger<OpenRouterAgentService> Logger) Service(
        string openRouter, Dictionary<string, string?> settings, Dictionary<string, string> modelLists, ModelPolicy? policy = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(settings)
        {
            ["OpenRouter:ApiKey"] = "or-key",
            ["OpenRouter:BaseUrl"] = openRouter,
            ["OpenRouter:RetryDelay"] = "00:00:00.001",
        }).Build();
        var servers = new FakeServers(modelLists);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(servers, disposeHandler: false));
        var logger = new ListLogger<OpenRouterAgentService>();
        return (new OpenRouterAgentService(configuration, factory, policy ?? new ModelPolicy(0.25m, 0.60m, ZdrOnly: true, DisableReasoning: true), logger), servers, logger);
    }

    [Test]
    [Arguments("gateway:sgl/deepseek", "gateway", "sgl/deepseek")]
    [Arguments("ollama:llama3", "ollama", "llama3")]
    [Arguments("local:org/model:latest", "local", "org/model:latest")]
    public async Task Split_CustomId_GivesProviderAndModel(string id, string provider, string model)
    {
        await Assert.That(CustomProvider.Split(id)).IsEqualTo((provider, model));
    }

    [Test]
    [Arguments("deepseek/deepseek-chat")]
    [Arguments("meta-llama/llama-3.3-70b-instruct:free")]
    [Arguments("cheap/model:batch")]
    [Arguments("~typesafe/jev-latest")]
    [Arguments("gateway:")]
    [Arguments(":model")]
    public async Task Split_OpenRouterId_IsNotCustom(string id)
    {
        await Assert.That(CustomProvider.Split(id)).IsNull();
    }

    [Test]
    public async Task FromConfiguration_ReadsEachProviderWithABaseUrl()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Providers:gateway:BaseUrl"] = "https://gw.test/v1/",
            ["Providers:gateway:ApiKey"] = "k",
            ["Providers:gateway:Models"] = " sgl/deepseek , other ,",
            ["Providers:local:BaseUrl"] = "http://localhost:11434/v1",
            ["Providers:broken:ApiKey"] = "no url",
        }).Build();

        var providers = CustomProvider.FromConfiguration(configuration);

        await Assert.That(providers.Select(p => p.Name)).IsEquivalentTo(["gateway", "local"]);
        var gateway = providers.Single(p => p.Name == "gateway");
        await Assert.That(gateway.BaseUrl).IsEqualTo("https://gw.test/v1");
        await Assert.That(gateway.Models).IsEquivalentTo(["sgl/deepseek", "other"]);
        await Assert.That(providers.Single(p => p.Name == "local").Models.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CustomModel_GoesToItsProvider_WithoutPrefixOrOpenRouterExtras()
    {
        var (service, servers, _) = Service("http://or-route.test/api/v1", new()
        {
            ["Providers:gateway:BaseUrl"] = "http://gw-route.test/v1",
            ["Providers:gateway:ApiKey"] = "gw-key",
            ["Providers:gateway:Models"] = "sgl/deepseek",
        }, []);

        var reply = await service.GenerateResponseAsync("gateway:sgl/deepseek", 0.7, "system",
            [new ChatMessage("user", "[Alice]: look") { Images = [new ImagePart("image/png", [1, 2, 3])] }]);

        await Assert.That(reply).IsEqualTo("hello");
        var call = servers.Requests.Single();
        await Assert.That(call.Url).IsEqualTo("http://gw-route.test/v1/chat/completions");
        await Assert.That(call.Authorization).IsEqualTo("Bearer gw-key");
        using var body = JsonDocument.Parse(call.Body!);
        await Assert.That(body.RootElement.GetProperty("model").GetString()).IsEqualTo("sgl/deepseek");
        foreach (var openRouterOnly in new[] { "provider", "reasoning", "session_id" })
            await Assert.That(body.RootElement.TryGetProperty(openRouterOnly, out _)).IsFalse();
        await Assert.That(call.Body!).Contains("data:image/png;base64,AQID");
    }

    [Test]
    public async Task OpenRouterModel_StillGoesToOpenRouter_WithItsExtras()
    {
        var (service, servers, _) = Service("http://or-keep.test/api/v1", new()
        {
            ["Providers:gateway:BaseUrl"] = "http://gw-keep.test/v1",
            ["Providers:gateway:Models"] = "sgl/deepseek",
        }, new() { ["http://or-keep.test/api/v1/models"] = OpenRouterModels });

        await service.GenerateResponseAsync("deepseek/deepseek-chat", 0.7, "system", [new ChatMessage("user", "[Alice]: hi")]);

        var call = servers.Requests.Single(r => r.Url.EndsWith("/chat/completions", StringComparison.Ordinal));
        await Assert.That(call.Url).IsEqualTo("http://or-keep.test/api/v1/chat/completions");
        await Assert.That(call.Authorization).IsEqualTo("Bearer or-key");
        using var body = JsonDocument.Parse(call.Body!);
        await Assert.That(body.RootElement.GetProperty("model").GetString()).IsEqualTo("deepseek/deepseek-chat");
        await Assert.That(body.RootElement.GetProperty("provider").GetProperty("zdr").GetBoolean()).IsTrue();
        await Assert.That(body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString()).IsEqualTo("none");
    }

    [Test]
    public async Task Catalogue_MergesConfiguredAndFetchedModels_AndLeavesOutAProviderThatIsDown()
    {
        var (service, servers, logger) = Service("http://or-merge.test/api/v1", new()
        {
            ["Providers:listed:BaseUrl"] = "http://listed-merge.test/v1",
            ["Providers:listed:Models"] = "sgl/deepseek",
            ["Providers:fetched:BaseUrl"] = "http://fetched-merge.test/v1",
            ["Providers:down:BaseUrl"] = "http://down-merge.test/v1",
        }, new()
        {
            ["http://or-merge.test/api/v1/models"] = OpenRouterModels,
            ["http://fetched-merge.test/v1/models"] = """{"data":[{"id":"qwen/qwen3","max_model_len":32768,"description":"A model. More text."}]}""",
        });

        var models = await service.ListModelsAsync();

        await Assert.That(models.Select(m => m.Id)).IsEquivalentTo(["deepseek/deepseek-chat", "listed:sgl/deepseek", "fetched:qwen/qwen3"]);
        var listed = models.Single(m => m.Id == "listed:sgl/deepseek");
        await Assert.That(listed.IsCustom).IsTrue();
        await Assert.That(listed.Provider).IsEqualTo("listed");
        await Assert.That(listed.Name).IsEqualTo("listed: sgl/deepseek");
        await Assert.That(listed.Pricing).IsEqualTo(new ModelPricing(0, 0));
        await Assert.That(listed.Capabilities).DoesNotContain(ModelMetadata.Vision);
        var fetched = models.Single(m => m.Id == "fetched:qwen/qwen3");
        await Assert.That(fetched.ContextLength).IsEqualTo(32768);
        await Assert.That(fetched.Description).IsEqualTo("A model.");
        // A configured list is not fetched; the provider that is down is logged once and left out.
        await Assert.That(servers.Requests.Any(r => r.Url.StartsWith("http://listed-merge.test", StringComparison.Ordinal))).IsFalse();
        await Assert.That(logger.Entries.Count(e => e.Message.Contains("down", StringComparison.Ordinal))).IsEqualTo(1);

        await service.ListModelsAsync();
        await Assert.That(servers.Requests.Count(r => r.Url.StartsWith("http://down-merge.test", StringComparison.Ordinal))).IsEqualTo(1);
    }

    private static readonly OpenRouterModel Custom = new("gateway:sgl/deepseek", "gateway: sgl/deepseek", null, new ModelPricing(0, 0)) { IsCustom = true };
    private static readonly OpenRouterModel Pricey = new("openai/gpt-5", "OpenAI: GPT-5", 400000, new ModelPricing(1.25m, 10m));

    private static IOpenRouterService Catalogue(params OpenRouterModel[] models)
    {
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns(models.ToList());
        openRouter.ListZdrModelIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        return openRouter;
    }

    [Test]
    public async Task Policy_DoesNotFilterCustomModels()
    {
        var policy = ModelPolicy.FromConfiguration(new ConfigurationBuilder().Build());
        var openRouter = Catalogue(Custom, Pricey);

        var allowed = await policy.FilterAsync(openRouter, await openRouter.ListModelsAsync());

        await Assert.That(allowed.Select(m => m.Id)).IsEquivalentTo([Custom.Id]);
        await Assert.That((await policy.StatusLookupAsync(openRouter))(Custom.Id).IsAvailable).IsTrue();
        await Assert.That(await policy.CheckModelAsync(openRouter, Custom.Id)).IsNull();
    }

    [Test]
    public async Task Policy_RefusesACustomModelTheProviderDoesNotOffer()
    {
        var policy = new ModelPolicy(0, 0);
        var openRouter = Catalogue(Custom);

        await Assert.That(await policy.CheckModelAsync(openRouter, "gateway:gone")).Contains("not offered by provider 'gateway'");
        await Assert.That((await policy.StatusLookupAsync(openRouter))("gateway:gone").Status).IsEqualTo(ModelStatus.Missing);
    }

    [Test]
    public async Task DefaultModel_IsTheConfiguredOne_ThenTheFirstCustom_ThenNone()
    {
        await Assert.That(CustomProvider.DefaultModel(Pricey.Id, [Custom, Pricey])).IsEqualTo(Pricey.Id);
        await Assert.That(CustomProvider.DefaultModel("not/offered", [Pricey, Custom])).IsEqualTo(Custom.Id);
        await Assert.That(CustomProvider.DefaultModel(null, [Pricey, Custom])).IsEqualTo(Custom.Id);
        await Assert.That(CustomProvider.DefaultModel(null, [Pricey])).IsNull();
    }

    [Test]
    public async Task ModelList_MarksTheDefault()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:DefaultModel"] = Pricey.Id }).Build();
        var handler = new ListOpenRouterModelsHandler(Catalogue(Custom, Pricey), new ModelPolicy(0, 0), configuration);

        var models = (await handler.Handle(new ListOpenRouterModelsQuery(), CancellationToken.None)).Value!;

        await Assert.That(models.Where(m => m.IsDefault).Select(m => m.Id)).IsEquivalentTo([Pricey.Id]);
    }
}
