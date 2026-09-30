using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    /// which stays around 150-250, so the slack is free. It stays this high
    /// with reasoning turned off too, since OpenRouter:DisableReasoning can be
    /// switched back. Overridable through OpenRouter:MaxOutputTokens.
    /// </summary>
    private const int DefaultMaxOutputTokens = 1500;

    private readonly OpenAIClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ModelPolicy _policy;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly int _maxOutputTokens;
    private readonly ILogger<OpenRouterAgentService> _logger;

    public OpenRouterAgentService(IConfiguration configuration, IHttpClientFactory httpClientFactory, ModelPolicy policy, ILogger<OpenRouterAgentService> logger)
    {
        _logger = logger;
        _apiKey = configuration["OpenRouter:ApiKey"]
            ?? throw new InvalidOperationException("OpenRouter:ApiKey is not configured.");
        _httpClientFactory = httpClientFactory;
        _policy = policy;
        _maxOutputTokens = configuration.GetValue("OpenRouter:MaxOutputTokens", DefaultMaxOutputTokens);
        // Overridable so the outgoing request can be captured against a
        // local listener, and so a gateway can be put in front.
        _baseUrl = configuration["OpenRouter:BaseUrl"] ?? DefaultBaseUrl;
        _client = new OpenAIClient(
            new ApiKeyCredential(_apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(_baseUrl),
                Transport = new HttpClientPipelineTransport(httpClientFactory.CreateClient(nameof(OpenRouterAgentService))),
                RetryPolicy = new ProviderRetryPolicy(configuration.GetValue("OpenRouter:RetryDelay", TimeSpan.FromSeconds(1)), logger),
            });
    }

    public async Task<string> GenerateResponseAsync(string modelId, double temperature, string systemPrompt, List<ChatMessage> history, CancellationToken ct = default)
    {
        AIAgent agent = _client
            .GetChatClient(modelId)
            .AsIChatClient()
            .AsAIAgent(instructions: systemPrompt);

        var cacheUpTo = MarksCache(modelId) ? history.FindLastIndex(m => m.Role != ChatMessage.Note) : -1;
        var messages = history
            .Select(m => new AgentChatMessage(
                m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
                [new TextContent(m.Content), .. m.Images.Select(i => new DataContent(i.Data, i.ContentType))]))
            .ToList();

        var effort = await ReasoningEffortAsync(modelId, ct);

        var options = new ChatClientAgentRunOptions(new ChatOptions
        {
            Temperature = (float)temperature,
            MaxOutputTokens = _maxOutputTokens,
            // ChatOptions.AdditionalProperties does not reach the wire through
            // this client - verified against a local listener, which saw only
            // max_tokens - so the field is patched onto the provider's own
            // options instead. Patch is marked experimental by the OpenAI SDK.
            RawRepresentationFactory = _ =>
            {
#pragma warning disable SCME0001
                var raw = new ChatCompletionOptions();
                if (effort is not null)
                    raw.Patch.Set("$.reasoning.effort"u8, effort);
                if (cacheUpTo >= 0)
                    raw.Patch.Set("$.messages"u8, MessagesWithCacheBreakpoint(systemPrompt, history, cacheUpTo));
                // The model was checked against the policy when the bot was
                // saved, but a model routes to several providers at different
                // prices and retention terms, so both also have to hold per
                // request: OpenRouter then only routes to endpoints that match.
                if (_policy.MaxPromptPrice > 0)
                    raw.Patch.Set("$.provider.max_price.prompt"u8, _policy.MaxPromptPrice);
                if (_policy.MaxCompletionPrice > 0)
                    raw.Patch.Set("$.provider.max_price.completion"u8, _policy.MaxCompletionPrice);
                if (_policy.ZdrOnly)
                    raw.Patch.Set("$.provider.zdr"u8, true);
#pragma warning restore SCME0001
                return raw;
            },
        });

        try
        {
            var response = await agent.RunAsync(messages, options: options, cancellationToken: ct);
            // OpenRouter reports usage on every response; logged so the prompt
            // cache hit rate can be read off the logs.
            if (response.Usage is { } usage)
                _logger.LogInformation(
                    "Reply from {ModelId}: {PromptTokens} prompt tokens ({CachedTokens} cached), {CompletionTokens} completion tokens ({ReasoningTokens} reasoning)",
                    modelId, usage.InputTokenCount ?? 0, usage.CachedInputTokenCount ?? 0, usage.OutputTokenCount ?? 0, usage.ReasoningTokenCount ?? 0);
            return response.Text ?? string.Empty;
        }
        catch (ClientResultException ex) when (ProviderRetryPolicy.IsRetried(ex.Status))
        {
            // Busy, not broken: a warning without the stack trace says all there is.
            _logger.LogWarning("OpenRouter still answered {Status} for {ModelId} after {MaxRetries} retries", ex.Status, modelId, ProviderRetryPolicy.MaxRetries);
            throw new ProviderBusyException(modelId, ex.Status, ex);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            // A bot saved before the policy was tightened keeps its model, but
            // OpenRouter refuses it once no endpoint satisfies zdr / max_price.
            throw new InvalidOperationException(
                $"OpenRouter found no endpoint for '{modelId}' that satisfies the model policy " +
                "(OpenRouter:ZdrOnly, OpenRouter:MaxPromptPrice, OpenRouter:MaxCompletionPrice). " +
                "Switch the bot to an allowed model or relax the policy.", ex);
        }
    }

    /// <summary>
    /// DeepSeek, OpenAI, Grok and others cache a repeated prompt start on their
    /// own; Anthropic and Gemini only cache up to a <c>cache_control</c> marker.
    /// </summary>
    private static bool MarksCache(string modelId) =>
        modelId.StartsWith("anthropic/", StringComparison.Ordinal) || modelId.StartsWith("google/", StringComparison.Ordinal);

    /// <summary>
    /// The request's messages with a <c>cache_control</c> breakpoint on turn
    /// <paramref name="breakpoint"/>, the last one before the note, so the
    /// system prompt and the history are cached and only the note is not.
    /// </summary>
    /// <remarks>
    /// Microsoft.Extensions.AI has no way to put the marker on a message part,
    /// and the OpenAI SDK's own message passed through as the raw
    /// representation did not reach the wire, so the whole list is written
    /// here and patched over the one the client serialises.
    /// </remarks>
    public static BinaryData MessagesWithCacheBreakpoint(string systemPrompt, List<ChatMessage> history, int breakpoint) =>
        BinaryData.FromObjectAsJson(history
            .Select((m, i) => new
            {
                role = m.Role == "assistant" ? "assistant" : "user",
                content = i == breakpoint || m.Images.Count > 0 ? (object)Parts(m, i == breakpoint) : m.Content,
            })
            .Prepend(new { role = "system", content = (object)systemPrompt }));

    /// <summary>A turn as content parts: the text, then its images, with the cache marker on the last part.</summary>
    private static List<Dictionary<string, object>> Parts(ChatMessage message, bool breakpoint)
    {
        var parts = new List<Dictionary<string, object>> { new() { ["type"] = "text", ["text"] = message.Content } };
        parts.AddRange(message.Images.Select(image => new Dictionary<string, object>
        {
            ["type"] = "image_url",
            ["image_url"] = new { url = image.DataUrl },
        }));
        if (breakpoint)
            parts[^1]["cache_control"] = new { type = "ephemeral" };
        return parts;
    }

    /// <summary>
    /// Bots are chat participants, not problem solvers: reasoning only makes a
    /// reply slower and dearer. So under OpenRouter:DisableReasoning a model that
    /// takes the reasoning parameter is told "none" and any other gets nothing
    /// (models that always reason reject "none", and the policy keeps them out).
    /// </summary>
    /// <remarks>
    /// With reasoning allowed, the effort is capped at low: reasoning models
    /// expand to fill the budget they are given, and unhinted one model spent
    /// every token reasoning and returned nothing. The provider's own
    /// reasoning.max_tokens is not reliably honoured - it overran a 120 cap to
    /// 400 in testing - so effort plus headroom is what works.
    /// </remarks>
    private async Task<string?> ReasoningEffortAsync(string modelId, CancellationToken ct)
    {
        if (!_policy.DisableReasoning)
            return "low";

        try
        {
            var model = (await ListModelsAsync(ct)).FirstOrDefault(m => m.Id == modelId);
            return model?.AcceptsReasoning == true ? "none" : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without the catalogue it is unknown whether the model takes the
            // parameter, and sending it to one that does not is what can fail.
            return null;
        }
    }

    /// <summary>
    /// The ZDR endpoint list is the same for every request and changes rarely,
    /// so it is fetched once per <see cref="ZdrCacheDuration"/> and shared.
    /// </summary>
    private static readonly TimeSpan ZdrCacheDuration = TimeSpan.FromHours(6);
    private static (DateTimeOffset FetchedAt, IReadOnlySet<string> Ids)? _zdrCache;

    public async Task<IReadOnlySet<string>> ListZdrModelIdsAsync(CancellationToken ct = default)
    {
        if (_zdrCache is { } cached && DateTimeOffset.UtcNow - cached.FetchedAt < ZdrCacheDuration)
            return cached.Ids;

        using var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/endpoints/zdr");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("data", out var data))
            foreach (var endpoint in data.EnumerateArray())
                if (endpoint.TryGetProperty("model_id", out var id) && id.GetString() is { } modelId)
                    ids.Add(modelId);

        _zdrCache = (DateTimeOffset.UtcNow, ids);
        return ids;
    }

    /// <summary>
    /// The catalogue is read on every bot list and before every reply round,
    /// so it is fetched once per <see cref="ModelsCacheDuration"/> and shared.
    /// </summary>
    private static readonly TimeSpan ModelsCacheDuration = TimeSpan.FromHours(1);
    private static (DateTimeOffset FetchedAt, List<OpenRouterModel> Models)? _modelsCache;

    /// <summary>
    /// Plain REST against OpenRouter's model catalogue. This is not an inference
    /// call, so it stays off the agent pipeline.
    /// </summary>
    public async Task<List<OpenRouterModel>> ListModelsAsync(CancellationToken ct = default)
    {
        if (_modelsCache is { } cached && DateTimeOffset.UtcNow - cached.FetchedAt < ModelsCacheDuration)
            return [.. cached.Models];

        using var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/models");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var models = ParseModels(await response.Content.ReadAsStringAsync(ct));
        _modelsCache = (DateTimeOffset.UtcNow, models);
        return [.. models];
    }

    public static List<OpenRouterModel> ParseModels(string json)
    {
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
                var description = item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                var inputs = item.TryGetProperty("architecture", out var architecture) && architecture.ValueKind == JsonValueKind.Object
                    ? Strings(architecture, "input_modalities")
                    : [];

                var parameters = Strings(item, "supported_parameters");
                models.Add(new OpenRouterModel(id, name, contextLength, ParsePricing(item))
                {
                    Description = ModelMetadata.Summary(description),
                    Capabilities = ModelMetadata.Capabilities(id, name, parameters, inputs),
                    AcceptsReasoning = parameters.Contains("reasoning"),
                    ReasoningMandatory = item.TryGetProperty("reasoning", out var reasoning)
                        && reasoning.ValueKind == JsonValueKind.Object
                        && reasoning.TryGetProperty("mandatory", out var mandatory)
                        && mandatory.ValueKind == JsonValueKind.True,
                });
            }
        }

        return models;
    }

    private static List<string> Strings(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : [];

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
