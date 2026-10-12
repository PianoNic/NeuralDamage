using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>
/// What every reply request carries whoever asks (bots, the chat summary, the
/// image describer, all through GenerateResponseAsync): a temperature no
/// higher than 1.5, none for a model that takes none, and reasoning off.
/// </summary>
public class GenerationSettingsTests
{
    private const string Completion = """
        {"id":"c","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"hello","reasoning_content":"let me think"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
        """;

    private const string Catalogue = """
        {"data":[
          {"id":"a/reasons","name":"Reasons","pricing":{"prompt":"0","completion":"0"},"supported_parameters":["reasoning","temperature"]},
          {"id":"a/plain","name":"Plain","pricing":{"prompt":"0","completion":"0"},"supported_parameters":["temperature","max_tokens"]},
          {"id":"a/fixed","name":"Fixed temperature","pricing":{"prompt":"0","completion":"0"},"supported_parameters":["reasoning","max_tokens"]}
        ]}
        """;

    private sealed class FakeServers(string? catalogue) : HttpMessageHandler
    {
        public List<(string Url, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Requests)
                Requests.Add((url, body));
            var json = url.EndsWith("/chat/completions", StringComparison.Ordinal) ? Completion
                : url.EndsWith("/models", StringComparison.Ordinal) ? catalogue
                : null;
            return json is null
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Unique hosts per test, since the catalogue cache is shared by URL; a null catalogue is unreachable.</summary>
    private static (OpenRouterAgentService Service, FakeServers Servers, ListLogger<OpenRouterAgentService> Logger) Service(string host, string? catalogue = Catalogue)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenRouter:ApiKey"] = "or-key",
            ["OpenRouter:BaseUrl"] = $"http://or-{host}.test/api/v1",
            ["OpenRouter:RetryDelay"] = "00:00:00.001",
            ["Providers:gateway:BaseUrl"] = $"http://gw-{host}.test/v1",
            ["Providers:gateway:Models"] = "sgl/deepseek",
        }).Build();
        var servers = new FakeServers(catalogue);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(servers, disposeHandler: false));
        var logger = new ListLogger<OpenRouterAgentService>();
        return (new OpenRouterAgentService(configuration, factory, new ModelPolicy(0, 0, DisableReasoning: true), logger), servers, logger);
    }

    private static async Task<JsonElement> SentAsync(OpenRouterAgentService service, FakeServers servers, string modelId, double temperature)
    {
        var reply = await service.GenerateResponseAsync(modelId, temperature, "system", [new ChatMessage("user", "[Alice]: hi")]);
        // The content only, never the reasoning a provider still returns.
        await Assert.That(reply).IsEqualTo("hello");
        var body = servers.Requests.Single(r => r.Url.EndsWith("/chat/completions", StringComparison.Ordinal)).Body!;
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Test]
    [Arguments("a/reasons")]
    [Arguments("gateway:sgl/deepseek")]
    public async Task ABotSavedAt2_IsSent1Point5_AndItIsLogged(string modelId)
    {
        var (service, servers, logger) = Service($"clamp-{modelId.Replace('/', '-').Replace(':', '-')}");

        var body = await SentAsync(service, servers, modelId, 2.0);

        await Assert.That(body.GetProperty("temperature").GetDouble()).IsEqualTo(1.5);
        await Assert.That(logger.Entries.Count(e => e.Level == LogLevel.Debug && e.Message.Contains("capped at 1.5", StringComparison.Ordinal))).IsEqualTo(1);
    }

    [Test]
    public async Task ATemperatureUnderTheCap_IsSentAsItIs()
    {
        var (service, servers, logger) = Service("under");

        var body = await SentAsync(service, servers, "a/plain", 0.3);

        await Assert.That(body.GetProperty("temperature").GetDouble()).IsEqualTo(0.3).Within(1e-6);
        await Assert.That(logger.Entries.Any(e => e.Message.Contains("capped", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task AModelWithoutTheTemperatureParameter_IsSentNone()
    {
        var (service, servers, _) = Service("fixed");

        var body = await SentAsync(service, servers, "a/fixed", 0.7);

        await Assert.That(body.TryGetProperty("temperature", out _)).IsFalse();
        await Assert.That(body.GetProperty("reasoning").GetProperty("effort").GetString()).IsEqualTo("none");
    }

    [Test]
    public async Task ACustomProvider_IsToldNotToThink_AndGetsNoOpenRouterReasoning()
    {
        var (service, servers, _) = Service("custom");

        var body = await SentAsync(service, servers, "gateway:sgl/deepseek", 0.7);

        var kwargs = body.GetProperty("chat_template_kwargs");
        await Assert.That(kwargs.GetProperty("thinking").GetBoolean()).IsFalse();
        await Assert.That(kwargs.GetProperty("enable_thinking").GetBoolean()).IsFalse();
        await Assert.That(body.TryGetProperty("reasoning", out _)).IsFalse();
        await Assert.That(body.GetProperty("temperature").GetDouble()).IsEqualTo(0.7).Within(1e-6);
    }

    [Test]
    public async Task AnOpenRouterModelThatTakesReasoning_IsSentEffortNone_AndNoTemplateKwargs()
    {
        var (service, servers, _) = Service("effort");

        var body = await SentAsync(service, servers, "a/reasons", 0.7);

        await Assert.That(body.GetProperty("reasoning").GetProperty("effort").GetString()).IsEqualTo("none");
        await Assert.That(body.GetProperty("reasoning").TryGetProperty("enabled", out _)).IsFalse();
        await Assert.That(body.GetProperty("temperature").GetDouble()).IsEqualTo(0.7).Within(1e-6);
        await Assert.That(body.TryGetProperty("chat_template_kwargs", out _)).IsFalse();
    }

    [Test]
    [Arguments("plain", "a/plain", Catalogue)]
    [Arguments("unlisted", "a/unlisted", Catalogue)]
    [Arguments("down", "a/reasons", null)]
    public async Task AnyOtherOpenRouterModel_IsSentReasoningDisabled(string host, string modelId, string? catalogue)
    {
        var (service, servers, _) = Service($"disabled-{host}", catalogue);

        var body = await SentAsync(service, servers, modelId, 0.7);

        await Assert.That(body.GetProperty("reasoning").GetProperty("enabled").GetBoolean()).IsFalse();
        await Assert.That(body.GetProperty("reasoning").TryGetProperty("effort", out _)).IsFalse();
        // Unknown to the catalogue, the temperature still goes.
        await Assert.That(body.GetProperty("temperature").GetDouble()).IsEqualTo(0.7).Within(1e-6);
    }

    [Test]
    public async Task Catalogue_ReadsWhetherAModelTakesATemperature()
    {
        var models = OpenRouterAgentService.ParseModels(Catalogue);

        await Assert.That(models.Single(m => m.Id == "a/plain").AcceptsTemperature).IsTrue();
        await Assert.That(models.Single(m => m.Id == "a/fixed").AcceptsTemperature).IsFalse();
    }
}
