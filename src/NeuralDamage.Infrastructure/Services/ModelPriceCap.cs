using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// The most a bot's model may cost, in dollars per million tokens. Zero on
/// either side means that side is not capped.
/// </summary>
public record ModelPriceCap(decimal MaxPromptPrice, decimal MaxCompletionPrice)
{
    public static ModelPriceCap FromConfiguration(IConfiguration configuration) => new(
        configuration.GetValue<decimal>("OpenRouter:MaxPromptPrice"),
        configuration.GetValue<decimal>("OpenRouter:MaxCompletionPrice"));

    public bool IsUnlimited => MaxPromptPrice <= 0 && MaxCompletionPrice <= 0;

    /// <summary>
    /// A model with no pricing, or variable pricing (OpenRouter reports -1 for
    /// routers such as openrouter/auto), cannot be checked, so it is refused
    /// whenever a cap is set.
    /// </summary>
    public bool Allows(ModelPricing? pricing)
    {
        if (IsUnlimited) return true;
        if (pricing is null || pricing.Prompt < 0 || pricing.Completion < 0) return false;
        if (MaxPromptPrice > 0 && pricing.Prompt > MaxPromptPrice) return false;
        if (MaxCompletionPrice > 0 && pricing.Completion > MaxCompletionPrice) return false;
        return true;
    }

    /// <summary>Looks the model up in OpenRouter's catalogue; unknown models are refused.</summary>
    public async Task<bool> AllowsModelAsync(IOpenRouterService openRouter, string modelId, CancellationToken ct = default)
    {
        if (IsUnlimited) return true;
        var models = await openRouter.ListModelsAsync(ct);
        return Allows(models.FirstOrDefault(m => m.Id == modelId)?.Pricing);
    }
}
