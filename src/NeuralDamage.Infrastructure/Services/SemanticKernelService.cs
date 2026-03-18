using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NeuralDamage.Application.Interfaces;

namespace NeuralDamage.Infrastructure.Services;

public class SemanticKernelService : IOpenRouterService
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient = new();

    public SemanticKernelService(IConfiguration configuration)
    {
        _apiKey = configuration["OpenRouter:ApiKey"] ?? throw new InvalidOperationException("OpenRouter:ApiKey is not configured.");
    }

    public async Task<string> GenerateResponseAsync(string modelId, double temperature, string systemPrompt, List<ChatMessage> history, CancellationToken ct = default)
    {
        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId: modelId,
                apiKey: _apiKey,
                httpClient: new HttpClient { BaseAddress = new Uri("https://openrouter.ai/api/v1/") })
            .Build();

        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var chatHistory = new ChatHistory(systemPrompt);

        foreach (var msg in history)
        {
            if (msg.Role == "assistant")
                chatHistory.AddAssistantMessage(msg.Content);
            else
                chatHistory.AddUserMessage(msg.Content);
        }

        var settings = new PromptExecutionSettings
        {
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"] = temperature,
                ["max_tokens"] = 256
            }
        };

        var response = await chat.GetChatMessageContentAsync(chatHistory, settings, cancellationToken: ct);
        return response.Content ?? string.Empty;
    }

    public async Task<List<OpenRouterModel>> ListModelsAsync(CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/models");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var models = new List<OpenRouterModel>();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? "";
                var name = item.GetProperty("name").GetString() ?? id;
                int? contextLength = item.TryGetProperty("context_length", out var cl) ? cl.GetInt32() : null;
                models.Add(new OpenRouterModel(id, name, contextLength));
            }
        }

        return models;
    }
}
