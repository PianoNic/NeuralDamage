using System.Text.Json.Serialization;
using NeuralDamage.Infrastructure.Models;

namespace NeuralDamage.Infrastructure.Services;

public record ChatMessage(string Role, string Content);
/// <summary>Dollars per million tokens.</summary>
public record ModelPricing(decimal Prompt, decimal Completion);

public record OpenRouterModel(string Id, string Name, int? ContextLength, ModelPricing? Pricing = null)
{
    /// <summary>The vendor's display name, e.g. DeepSeek for <c>deepseek/...</c>.</summary>
    public string Provider { get; init; } = ModelMetadata.Provider(Id);

    /// <summary>The first sentence of OpenRouter's description.</summary>
    public string? Description { get; init; }

    /// <summary>Any of reasoning, vision, code and fast.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = ModelMetadata.Capabilities(Id, Name);

    /// <summary>1 (cheap) to 3 (expensive), relative to the configured price caps.</summary>
    public int PriceTier { get; init; }

    /// <summary>Whether the model takes OpenRouter's <c>reasoning</c> parameter, so its effort can be set.</summary>
    [JsonIgnore]
    public bool AcceptsReasoning { get; init; }

    /// <summary>Whether the model always reasons (<c>reasoning.mandatory</c>) and rejects an effort of none.</summary>
    [JsonIgnore]
    public bool ReasoningMandatory { get; init; }
}

public interface IOpenRouterService
{
    Task<string> GenerateResponseAsync(string modelId, double temperature, string systemPrompt, List<ChatMessage> history, CancellationToken ct = default);
    Task<List<OpenRouterModel>> ListModelsAsync(CancellationToken ct = default);
    /// <summary>Ids of the models that have at least one zero-data-retention endpoint.</summary>
    Task<IReadOnlySet<string>> ListZdrModelIdsAsync(CancellationToken ct = default);
}
