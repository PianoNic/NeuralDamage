using System.ClientModel;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using AgentChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// Talks to OpenRouter through the Microsoft Agent Framework, which supersedes
/// Semantic Kernel. OpenRouter speaks the OpenAI wire protocol, so the OpenAI
/// client works against it once the endpoint is repointed.
/// </summary>
public class OpenRouterAgentService : IOpenRouterService
{
    private const string BaseUrl = "https://openrouter.ai/api/v1";

    private readonly OpenAIClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;

    public OpenRouterAgentService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _apiKey = configuration["OpenRouter:ApiKey"]
            ?? throw new InvalidOperationException("OpenRouter:ApiKey is not configured.");
        _httpClientFactory = httpClientFactory;
        _client = new OpenAIClient(
            new ApiKeyCredential(_apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(BaseUrl) });
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
            MaxOutputTokens = 256,
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
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
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
                models.Add(new OpenRouterModel(id, name, contextLength));
            }
        }

        return models;
    }
}
