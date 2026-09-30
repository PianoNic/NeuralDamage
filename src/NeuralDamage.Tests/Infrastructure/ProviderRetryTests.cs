using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Services;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>
/// A provider that is rate limited or overloaded is asked again, a couple of
/// times, before the reply counts as failed.
/// </summary>
public class ProviderRetryTests
{
    private const string Completion = """
        {"id":"c","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
        """;

    /// <summary>Answers each chat completion with the next status in line, then with a reply.</summary>
    private sealed class ScriptedHandler(params (HttpStatusCode Status, string? RetryAfter)[] script) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var call = Calls++;
            if (call >= script.Length)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Completion, Encoding.UTF8, "application/json") });
            var (status, retryAfter) = script[call];
            var response = new HttpResponseMessage(status) { Content = new StringContent("""{"error":{"message":"busy","code":429}}""", Encoding.UTF8, "application/json") };
            if (retryAfter is not null)
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            return Task.FromResult(response);
        }
    }

    private sealed class ListLogger : ILogger<OpenRouterAgentService>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private static OpenRouterAgentService Service(HttpMessageHandler handler, ListLogger logger)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenRouter:ApiKey"] = "test",
            ["OpenRouter:BaseUrl"] = "http://openrouter.test/api/v1",
            ["OpenRouter:RetryDelay"] = "00:00:00.001",
        }).Build();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return new OpenRouterAgentService(configuration, factory, new ModelPolicy(0, 0), logger);
    }

    private static Task<string> Generate(OpenRouterAgentService service) =>
        service.GenerateResponseAsync("mistralai/mistral-nemo", 0.7, "system", [new ChatMessage("user", "[Alice]: hi")]);

    [Test]
    public async Task RateLimitedTwice_ThenAnswers()
    {
        var handler = new ScriptedHandler((HttpStatusCode.TooManyRequests, "0"), ((HttpStatusCode)529, null));
        var logger = new ListLogger();

        var reply = await Generate(Service(handler, logger));

        await Assert.That(reply).IsEqualTo("hello");
        await Assert.That(handler.Calls).IsEqualTo(3);
        await Assert.That(logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Exception is null)).IsEqualTo(2);
    }

    [Test]
    public async Task StillRateLimitedAfterTwoRetries_FailsWithAOneLineWarning()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.TooManyRequests, "0"), (HttpStatusCode.ServiceUnavailable, null), (HttpStatusCode.TooManyRequests, "0"));
        var logger = new ListLogger();

        var failure = await Assert.ThrowsAsync<ProviderBusyException>(() => Generate(Service(handler, logger)));

        await Assert.That(handler.Calls).IsEqualTo(3);
        await Assert.That(failure!.Status).IsEqualTo(429);
        await Assert.That(failure.ToString()).DoesNotContain(" at ");
        await Assert.That(logger.Entries.Any(e => e.Level >= LogLevel.Error || e.Exception is not null)).IsFalse();
    }

    [Test]
    public async Task ProviderAsksForALongWait_IsNotRetried()
    {
        var handler = new ScriptedHandler((HttpStatusCode.TooManyRequests, "60"));

        await Assert.ThrowsAsync<ProviderBusyException>(() => Generate(Service(handler, new ListLogger())));

        await Assert.That(handler.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task OtherErrors_AreNotRetried()
    {
        var handler = new ScriptedHandler((HttpStatusCode.BadRequest, null));

        await Assert.ThrowsAsync<System.ClientModel.ClientResultException>(() => Generate(Service(handler, new ListLogger())));

        await Assert.That(handler.Calls).IsEqualTo(1);
    }
}
