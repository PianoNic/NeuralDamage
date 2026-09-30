using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Infrastructure.Services;

public record ChatMessage(string Role, string Content);
/// <summary>Dollars per million tokens.</summary>
public record ModelPricing(decimal Prompt, decimal Completion);
public record OpenRouterModel(string Id, string Name, int? ContextLength, ModelPricing? Pricing = null);

public interface IOpenRouterService
{
    Task<string> GenerateResponseAsync(string modelId, double temperature, string systemPrompt, List<ChatMessage> history, CancellationToken ct = default);
    Task<List<OpenRouterModel>> ListModelsAsync(CancellationToken ct = default);
}
