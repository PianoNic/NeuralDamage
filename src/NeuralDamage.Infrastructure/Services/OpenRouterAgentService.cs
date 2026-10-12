using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
public partial class OpenRouterAgentService : IOpenRouterService
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

    /// <summary>The <c>Providers:*</c> endpoints by name, each with its own client.</summary>
    private readonly Dictionary<string, (CustomProvider Provider, OpenAIClient Client)> _providers;

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
        var retryDelay = configuration.GetValue("OpenRouter:RetryDelay", TimeSpan.FromSeconds(1));
        _client = Client(_baseUrl, _apiKey);
        _providers = CustomProvider.FromConfiguration(configuration)
            .ToDictionary(p => p.Name, p => (p, Client(p.BaseUrl, p.ApiKey)), StringComparer.OrdinalIgnoreCase);

        OpenAIClient Client(string baseUrl, string apiKey) => new(
            // A local endpoint may want no key, but the credential cannot be empty.
            new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? "none" : apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(baseUrl),
                Transport = new HttpClientPipelineTransport(httpClientFactory.CreateClient(nameof(OpenRouterAgentService))),
                RetryPolicy = new ProviderRetryPolicy(retryDelay, logger),
            });
    }

    /// <summary>
    /// The client and model name for <paramref name="modelId"/>: the
    /// <c>&lt;Name&gt;:&lt;model&gt;</c> id of a configured provider goes to that
    /// provider without the prefix, anything else to OpenRouter as it is.
    /// </summary>
    private (string? Provider, OpenAIClient Client, string Model) Route(string modelId) =>
        CustomProvider.Split(modelId) is { } split && _providers.TryGetValue(split.Provider, out var custom)
            ? (custom.Provider.Name, custom.Client, split.Model)
            : (null, _client, modelId);

    public async Task<string> GenerateResponseAsync(string modelId, double temperature, string systemPrompt, List<ChatMessage> history, CancellationToken ct = default)
    {
        var (provider, client, model) = Route(modelId);
        AIAgent agent = client
            .GetChatClient(model)
            .AsIChatClient()
            .AsAIAgent(instructions: systemPrompt);

        // Two breakpoints: on the message being answered, which a regenerate in
        // the same round reads back, and on the turn before it, which the next
        // round reads back - by then the answered message has lost its
        // "(you're answering this)" mark, so a cache ending on it never hits again.
        var last = MarksCache(modelId) ? history.FindLastIndex(m => m.Role != ChatMessage.Note) : -1;
        int[] breakpoints = last < 0 ? [] : last == 0 ? [0] : [last - 1, last];
        var messages = history
            .Select(m => new AgentChatMessage(
                m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
                [new TextContent(m.Content), .. m.Images.Select(i => new DataContent(i.Data, i.ContentType))]))
            .ToList();

        // A custom provider gets the plain OpenAI request: the extras below are
        // OpenRouter's own (reasoning, session, cache markers, routing).
        if (provider is not null)
            return await RunAsync(agent, messages, new ChatClientAgentRunOptions(
                new ChatOptions { Temperature = (float)temperature, MaxOutputTokens = _maxOutputTokens }), modelId, provider, ct);

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
                raw.Patch.Set("$.session_id"u8, SessionId(modelId, systemPrompt));
                if (breakpoints.Length > 0)
                    raw.Patch.Set("$.messages"u8, MessagesWithCacheBreakpoint(systemPrompt, history, breakpoints));
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

        return await RunAsync(agent, messages, options, modelId, null, ct);
    }

    /// <param name="provider">The custom provider's name, or null for OpenRouter.</param>
    private async Task<string> RunAsync(AIAgent agent, List<AgentChatMessage> messages, ChatClientAgentRunOptions options, string modelId, string? provider, CancellationToken ct)
    {
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
            _logger.LogWarning("{Provider} still answered {Status} for {ModelId} after {MaxRetries} retries", provider ?? "OpenRouter", ex.Status, modelId, ProviderRetryPolicy.MaxRetries);
            throw new ProviderBusyException(modelId, ex.Status, ex);
        }
        catch (ClientResultException ex) when (ex.Status == 404 && provider is null)
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
    /// OpenRouter spreads requests for a model over its providers, and a
    /// provider's prompt cache only helps the requests it gets: measured, a
    /// bot's three replies in a row went to three providers. A session id
    /// keeps a conversation on one provider. The system prompt is the same on
    /// every call for a bot in a chat, so with the model it names the session.
    /// </summary>
    public static string SessionId(string modelId, string systemPrompt) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{modelId}\n{systemPrompt}")))[..32].ToLowerInvariant();

    /// <summary>
    /// DeepSeek, OpenAI, Grok and others cache a repeated prompt start on their
    /// own; Anthropic and Gemini only cache up to a <c>cache_control</c> marker.
    /// </summary>
    private static bool MarksCache(string modelId) =>
        modelId.StartsWith("anthropic/", StringComparison.Ordinal) || modelId.StartsWith("google/", StringComparison.Ordinal);

    /// <summary>
    /// The request's messages with a <c>cache_control</c> breakpoint on each of
    /// the turns <paramref name="breakpoints"/>, so the system prompt and the
    /// history are cached and only the note is not.
    /// </summary>
    /// <remarks>
    /// Microsoft.Extensions.AI has no way to put the marker on a message part,
    /// and the OpenAI SDK's own message passed through as the raw
    /// representation did not reach the wire, so the whole list is written
    /// here and patched over the one the client serialises.
    /// <para>
    /// Every turn goes as content parts, not only the marked ones. Anthropic
    /// takes no two user turns in a row, so OpenRouter merges them, and a turn
    /// sent as a string one call and as parts the next came out different:
    /// measured on Claude Sonnet, the second reply read 0 cached tokens that
    /// way and 1876 of 1944 with parts throughout.
    /// </para>
    /// </remarks>
    public static BinaryData MessagesWithCacheBreakpoint(string systemPrompt, List<ChatMessage> history, params int[] breakpoints) =>
        BinaryData.FromObjectAsJson(history
            .Select((m, i) => new
            {
                role = m.Role == "assistant" ? "assistant" : "user",
                content = (object)Parts(m, breakpoints.Contains(i)),
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

    /// <summary>
    /// A custom provider that is down is asked again after this long, not on
    /// every call, and each ask waits at most <see cref="CustomModelsTimeout"/>.
    /// </summary>
    private static readonly TimeSpan CustomModelsRetry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CustomModelsTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Each catalogue by the URL it was read from.</summary>
    private static readonly ConcurrentDictionary<string, (DateTimeOffset Expires, List<OpenRouterModel> Models)> _modelsCache = new();

    /// <summary>
    /// OpenRouter's catalogue followed by every custom provider's models.
    /// Plain REST, not an inference call, so it stays off the agent pipeline.
    /// OpenRouter failing fails the call; a custom provider failing only
    /// leaves its models out.
    /// </summary>
    public async Task<List<OpenRouterModel>> ListModelsAsync(CancellationToken ct = default)
    {
        var models = await CachedAsync($"{_baseUrl}/models", async () =>
            ParseModels(await GetStringAsync($"{_baseUrl}/models", _apiKey, ct)), ModelsCacheDuration);

        foreach (var (provider, _) in _providers.Values)
            models.AddRange(provider.Models.Count > 0
                ? provider.Models.Select(m => CustomModel(provider, m, provider.ContextLength, null))
                : await CustomModelsAsync(provider, ct));
        return models;
    }

    private async Task<List<OpenRouterModel>> CustomModelsAsync(CustomProvider provider, CancellationToken ct)
    {
        var url = $"{provider.BaseUrl}/models";
        try
        {
            return await CachedAsync(url, async () =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(CustomModelsTimeout);
                return ParseCustomModels(provider, await GetStringAsync(url, provider.ApiKey, timeout.Token));
            }, ModelsCacheDuration);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Models of provider {Provider} unavailable, left out for {Minutes} minutes: {Error}", provider.Name, CustomModelsRetry.TotalMinutes, ex.Message);
            _modelsCache[url] = (DateTimeOffset.UtcNow + CustomModelsRetry, []);
            return [];
        }
    }

    private static async Task<List<OpenRouterModel>> CachedAsync(string url, Func<Task<List<OpenRouterModel>>> fetch, TimeSpan duration)
    {
        if (_modelsCache.TryGetValue(url, out var cached) && DateTimeOffset.UtcNow < cached.Expires)
            return [.. cached.Models];
        var models = await fetch();
        _modelsCache[url] = (DateTimeOffset.UtcNow + duration, models);
        return [.. models];
    }

    private async Task<string> GetStringAsync(string url, string apiKey, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// An OpenAI-style <c>/models</c> list: ids, and a context length or
    /// description where the server gives one (vLLM and SGLang call it max_model_len).
    /// </summary>
    public static List<OpenRouterModel> ParseCustomModels(CustomProvider provider, string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        return data.EnumerateArray()
            .Where(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(item =>
            {
                int? contextLength = (item.TryGetProperty("context_length", out var cl) || item.TryGetProperty("max_model_len", out cl))
                    && cl.ValueKind == JsonValueKind.Number && cl.TryGetInt32(out var length) ? length : null;
                var description = item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                return CustomModel(provider, item.GetProperty("id").GetString()!, provider.ContextLength ?? contextLength, description);
            })
            .ToList();
    }

    /// <summary>
    /// A custom provider's model as the catalogue lists it: free, named
    /// "Provider: model" like OpenRouter's "Vendor: model", and without vision,
    /// since nothing says it can see, so its bots get image descriptions.
    /// </summary>
    public static OpenRouterModel CustomModel(CustomProvider provider, string model, int? contextLength, string? description) =>
        new(provider.ModelId(model), $"{provider.Name}: {model}", contextLength, new ModelPricing(0, 0))
        {
            Provider = provider.Name,
            Description = ModelMetadata.Summary(description),
            Capabilities = ModelMetadata.Capabilities(model, model),
            IsCustom = true,
        };

    /// <summary>The context assumed when nothing says what a model has.</summary>
    public const int FallbackContextLength = 32_768;

    /// <summary>
    /// Comfortably past any context a provider here serves (about 300k tokens
    /// of "hi"), so the request is refused before anything is generated.
    /// </summary>
    private const int ProbeWords = 300_000;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Probed context lengths by provider URL and model, one probe each per process.</summary>
    private static readonly ConcurrentDictionary<string, Lazy<Task<int>>> _probed = new();

    /// <summary>
    /// The model's context, in tokens, from the first source that knows it:
    /// <c>Providers:&lt;Name&gt;:ContextLength</c>, the catalogue (OpenRouter's
    /// <c>context_length</c>, or a provider's <c>context_length</c> /
    /// <c>max_model_len</c>), a one-time probe of a custom provider, and
    /// <see cref="FallbackContextLength"/> when all of them come up empty.
    /// Never throws, except on cancellation.
    /// </summary>
    public async Task<int> ContextLengthAsync(string modelId, CancellationToken ct = default)
    {
        var (provider, _, model) = Route(modelId);
        var custom = provider is null ? null : _providers[provider].Provider;
        if (custom?.ContextLength is { } configured)
            return configured;

        try
        {
            // A custom provider's own /models, even when Models is configured:
            // it is only read for the length here, and OpenRouter being down
            // must not cost a probe.
            var listed = (custom is null ? await ListModelsAsync(ct) : await CustomModelsAsync(custom, ct))
                .FirstOrDefault(m => m.Id == modelId)?.ContextLength;
            if (listed is > 0)
                return listed.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Model catalogue unavailable for the context of {ModelId}: {Error}", modelId, ex.Message);
        }

        if (custom is null)
            return FallbackContextLength;
        return await _probed.GetOrAdd($"{custom.BaseUrl}|{model}", _ => new Lazy<Task<int>>(() => ProbeAsync(custom, model, modelId))).Value.WaitAsync(ct);
    }

    /// <summary>
    /// Asks <paramref name="provider"/> to complete a prompt too long for any
    /// model it serves, with one token of output, and reads the model's limit
    /// off the refusal ("maximum context length is 131072 tokens", as vLLM,
    /// SGLang and OpenAI word it). A prompt that is accepted, or a refusal
    /// that says no number, leaves the fallback.
    /// </summary>
    private async Task<int> ProbeAsync(CustomProvider provider, string model, string modelId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(ProbeTimeout);
            using var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl}/chat/completions")
            {
                Content = JsonContent(new
                {
                    model,
                    max_tokens = 1,
                    messages = new[] { new { role = "user", content = string.Concat(Enumerable.Repeat("hi ", ProbeWords)) } },
                }),
            };
            if (!string.IsNullOrEmpty(provider.ApiKey))
                request.Headers.Add("Authorization", $"Bearer {provider.ApiKey}");

            using var response = await client.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (ParseContextLength(body) is { } length)
            {
                _logger.LogInformation("Probed the context of {ModelId}: {ContextLength} tokens", modelId, length);
                return length;
            }
            _logger.LogWarning("Probing the context of {ModelId} gave {Status} without a length; assuming {ContextLength} tokens", modelId, (int)response.StatusCode, FallbackContextLength);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Probing the context of {ModelId} failed, assuming {ContextLength} tokens: {Error}", modelId, FallbackContextLength, ex.Message);
        }
        return FallbackContextLength;
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    /// <summary>The N of "maximum context length is N" in a provider's refusal, or null.</summary>
    public static int? ParseContextLength(string? error) =>
        error is not null && MaximumContextLength().Match(error) is { Success: true } match
            && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var length) && length > 0
            ? length
            : null;

    [GeneratedRegex(@"maximum context length is (\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MaximumContextLength();

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
