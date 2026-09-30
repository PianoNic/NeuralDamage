using System.ClientModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;
using AgentChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// Talks to OpenRouter through the Microsoft Agent Framework, which supersedes
/// Semantic Kernel. OpenRouter speaks the OpenAI wire protocol, so the OpenAI
/// client works against it once the endpoint is repointed.
/// </summary>
public class OpenRouterAgentService : IOpenRouterService
{
    private const string DefaultBaseUrl = "https://openrouter.ai/api/v1";

    /// <summary>
    /// Headroom, not a target. A reply is a few sentences, but a reasoning
    /// model spends this budget thinking first, and a cap tight enough to be
    /// "about right" for the reply starves it: at 400 one model burned the lot
    /// on reasoning and returned nothing. Billing is on tokens actually used,
    /// which stays around 150-250, so the slack is free. Overridable through
    /// OpenRouter:MaxOutputTokens.
    /// </summary>
    private const int DefaultMaxOutputTokens = 1500;

    private readonly OpenAIClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ModelPriceCap _priceCap;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly int _maxOutputTokens;

    public OpenRouterAgentService(IConfiguration configuration, IHttpClientFactory httpClientFactory, ModelPriceCap priceCap)
    {
        _apiKey = configuration["OpenRouter:ApiKey"]
            ?? throw new InvalidOperationException("OpenRouter:ApiKey is not configured.");
        _httpClientFactory = httpClientFactory;
        _priceCap = priceCap;
        _maxOutputTokens = configuration.GetValue("OpenRouter:MaxOutputTokens", DefaultMaxOutputTokens);
        // Overridable so the outgoing request can be captured against a
        // local listener, and so a gateway can be put in front.
        _baseUrl = configuration["OpenRouter:BaseUrl"] ?? DefaultBaseUrl;
        _client = new OpenAIClient(
            new ApiKeyCredential(_apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(_baseUrl) });
    }

    public async Task<string> GenerateResponseAsync(string modelId, double temperature, string systemPrompt, List<ChatMessage> history, CancellationToken ct = default)
    {
        AIAgent agent = _client
            .GetChatClient(modelId)
            .AsIChatClient()
            .AsAIAgent(instructions: systemPrompt);

        var messages = history
            .Select(m => new AgentChatMessage(
                m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
                m.Content))
            .ToList();

        var options = new ChatClientAgentRunOptions(new ChatOptions
        {
            Temperature = (float)temperature,
            MaxOutputTokens = _maxOutputTokens,
            // Reasoning models expand to fill the budget they are given, so the
            // effort has to be capped as well as the total: unhinted, one model
            // spent every token reasoning and returned nothing. With low effort
            // reasoning settles around 120-170 tokens. The provider's own
            // reasoning.max_tokens is not reliably honoured - it overran a 120
            // cap to 400 in testing - so effort plus headroom is what works.
            // Ignored by models that do not reason.
            //
            // ChatOptions.AdditionalProperties does not reach the wire through
            // this client - verified against a local listener, which saw only
            // max_tokens - so the field is patched onto the provider's own
            // options instead. Patch is marked experimental by the OpenAI SDK.
            RawRepresentationFactory = _ =>
            {
#pragma warning disable SCME0001
                var raw = new ChatCompletionOptions();
                raw.Patch.Set("$.reasoning.effort"u8, "low");
                // The model was checked against the cap when the bot was saved,
                // but a model routes to several providers at different prices,
                // so the cap also has to hold per request.
                if (_priceCap.MaxPromptPrice > 0)
                    raw.Patch.Set("$.provider.max_price.prompt"u8, _priceCap.MaxPromptPrice);
                if (_priceCap.MaxCompletionPrice > 0)
                    raw.Patch.Set("$.provider.max_price.completion"u8, _priceCap.MaxCompletionPrice);
#pragma warning restore SCME0001
                return raw;
            },
        });

        var response = await agent.RunAsync(messages, options: options, cancellationToken: ct);
        return response.Text ?? string.Empty;
    }

    /// <summary>
    /// Plain REST against OpenRouter's model catalogue. This is not an inference
    /// call, so it stays off the agent pipeline.
    /// </summary>
    public async Task<List<OpenRouterModel>> ListModelsAsync(CancellationToken ct = default)
    {
        using var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/models");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var models = new List<OpenRouterModel>();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? "";
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                int? contextLength = item.TryGetProperty("context_length", out var cl)
                    && cl.ValueKind == JsonValueKind.Number ? cl.GetInt32() : null;
                models.Add(new OpenRouterModel(id, name, contextLength, ParsePricing(item)));
            }
        }

        return models;
    }

    /// <summary>
    /// OpenRouter prices per token, as strings; the caps are per million tokens.
    /// </summary>
    private static ModelPricing? ParsePricing(JsonElement model)
    {
        if (!model.TryGetProperty("pricing", out var pricing)
            || !TryParsePrice(pricing, "prompt", out var prompt)
            || !TryParsePrice(pricing, "completion", out var completion))
            return null;
        return new ModelPricing(prompt * 1_000_000, completion * 1_000_000);
    }

    private static bool TryParsePrice(JsonElement pricing, string name, out decimal price)
    {
        price = 0;
        return pricing.ValueKind == JsonValueKind.Object
            && pricing.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out price);
    }
}
