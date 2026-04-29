using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Infrastructure.Services;

public record ChatMessage(string Role, string Content);
public record OpenRouterModel(string Id, string Name, int? ContextLength);

public interface IOpenRouterService
{
    Task<string> GenerateResponseAsync(string modelId, double temperature, string systemPrompt, List<ChatMessage> history, CancellationToken ct = default);
    Task<List<OpenRouterModel>> ListModelsAsync(CancellationToken ct = default);
}
