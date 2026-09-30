using System.Net;
using System.Text;
using System.Text.Json;
using NeuralDamage.Infrastructure.Services.BotDecision;
using Microsoft.Extensions.Logging.Abstractions;

namespace NeuralDamage.Tests.BotDecision;

public class DecisionsClientTests
{
    // Recorded from the OpenRouter "How to use Jev" tutorial.
    private const string RecordedResponse = """
        {
          "model": "typesafe/jev-1.13-20260917",
          "answers": {
            "offsite_transaction": { "type": "noul", "noul": 0.05 },
            "described_condition": {
              "type": "score",
              "score": 1,
              "legend": { "0": "Broken", "1": "Worn" },
              "probabilities": { "0": 0, "1": 1 },
              "confidence": 1
            }
          },
          "usage": { "input_tokens": 492, "output_tokens": 38, "cost": 0.000020664 },
          "id": "gen-dec-1790099229-ahAseXX5gNJzoLiCZICn",
          "provider": "TypeSafe"
        }
        """;

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly Dictionary<string, DecisionQuestion> Questions = new()
    {
        ["bot_0"] = new DecisionQuestion("noul", "Would `bots.bot_0` reply?", new NoulCriteria("yes", "no")),
    };

    private static DecisionsClient Client(StubHandler handler, string? apiKey = "sk-test") =>
        new(new HttpClient(handler), new BotRankingOptions { ApiKey = apiKey }, NullLogger<DecisionsClient>.Instance);

    [Test]
    public async Task RecordedResponse_Parses()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RecordedResponse);

        var response = await Client(handler).DecideAsync(new { NewMessage = "hi" }, Questions);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Answers!["offsite_transaction"].Noul).IsEqualTo(0.05);
        await Assert.That(response.Answers["described_condition"].Confidence).IsEqualTo(1);
        await Assert.That(response.Answers["described_condition"].Probabilities!["1"]).IsEqualTo(1);
        await Assert.That(response.Usage!.InputTokens).IsEqualTo(492);
        await Assert.That(response.Usage.Cost).IsEqualTo(0.000020664m);
    }

    [Test]
    public async Task Request_HasBearerKeyModelAndSnakeCaseBody()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RecordedResponse);

        await Client(handler).DecideAsync(new { NewMessage = "hi" }, Questions);

        await Assert.That(handler.Request!.RequestUri!.ToString()).IsEqualTo(BotRankingOptions.DefaultEndpoint);
        await Assert.That(handler.Request.Headers.Authorization!.ToString()).IsEqualTo("Bearer sk-test");

        using var body = JsonDocument.Parse(handler.RequestBody!);
        await Assert.That(body.RootElement.GetProperty("model").GetString()).IsEqualTo(BotRankingOptions.DefaultModel);
        await Assert.That(body.RootElement.GetProperty("state").GetProperty("new_message").GetString()).IsEqualTo("hi");
        var question = body.RootElement.GetProperty("questions").GetProperty("bot_0");
        await Assert.That(question.GetProperty("type").GetString()).IsEqualTo("noul");
        await Assert.That(question.GetProperty("criteria").GetProperty("true").GetString()).IsEqualTo("yes");
        await Assert.That(question.GetProperty("criteria").GetProperty("false").GetString()).IsEqualTo("no");
    }

    [Test]
    public async Task NonSuccessStatus_ReturnsNull()
    {
        var handler = new StubHandler(HttpStatusCode.TooManyRequests, """{"error":"rate limited"}""");

        await Assert.That(await Client(handler).DecideAsync(new { }, Questions)).IsNull();
    }

    [Test]
    public async Task NoApiKey_ReturnsNullWithoutCalling()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RecordedResponse);

        await Assert.That(await Client(handler, apiKey: null).DecideAsync(new { }, Questions)).IsNull();
        await Assert.That(handler.Request).IsNull();
    }
}
