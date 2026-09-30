using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// Which models a bot may use. The defaults are safe and cheap: only models
/// with a zero-data-retention endpoint, no <c>:batch</c> variants, and at most
/// 0.25 / 0.60 dollars per million prompt / completion tokens. A price of zero
/// on either side means that side is not capped.
/// </summary>
public record ModelPolicy(decimal MaxPromptPrice, decimal MaxCompletionPrice, bool ZdrOnly = false, bool ExcludeBatchModels = false)
{
    public const decimal DefaultMaxPromptPrice = 0.25m;
    public const decimal DefaultMaxCompletionPrice = 0.60m;

    public static ModelPolicy FromConfiguration(IConfiguration configuration) => new(
        Price(configuration, "OpenRouter:MaxPromptPrice", DefaultMaxPromptPrice),
        Price(configuration, "OpenRouter:MaxCompletionPrice", DefaultMaxCompletionPrice),
        configuration.GetValue("OpenRouter:ZdrOnly", true),
        configuration.GetValue("OpenRouter:ExcludeBatchModels", true));

    /// <summary>Unset (or blank, as an empty .env line gives) falls back to the default; an explicit 0 lifts the cap.</summary>
    private static decimal Price(IConfiguration configuration, string key, decimal fallback) =>
        string.IsNullOrWhiteSpace(configuration[key]) ? fallback : configuration.GetValue<decimal>(key);

    public bool IsPriceUnlimited => MaxPromptPrice <= 0 && MaxCompletionPrice <= 0;

    /// <summary>
    /// Why the model is not allowed, or null when it is. <paramref name="zdrModelIds"/>
    /// is only consulted under <see cref="ZdrOnly"/>.
    /// A model with no pricing, or variable pricing (OpenRouter reports -1 for
    /// routers such as openrouter/auto), cannot be checked, so it is refused
    /// whenever a price cap is set.
    /// </summary>
    public string? Refusal(string modelId, ModelPricing? pricing, IReadOnlySet<string> zdrModelIds)
    {
        if (ExcludeBatchModels && modelId.EndsWith(":batch", StringComparison.Ordinal))
            return $"Model '{modelId}' is a batch variant, which cannot answer in a live chat.";
        if (ZdrOnly && !zdrModelIds.Contains(modelId))
            return $"Model '{modelId}' has no zero-data-retention endpoint on OpenRouter.";
        if (IsPriceUnlimited)
            return null;
        if (pricing is null || pricing.Prompt < 0 || pricing.Completion < 0)
            return $"Model '{modelId}' has no fixed price, so it cannot be checked against the configured price cap.";
        if ((MaxPromptPrice > 0 && pricing.Prompt > MaxPromptPrice)
            || (MaxCompletionPrice > 0 && pricing.Completion > MaxCompletionPrice))
            return $"Model '{modelId}' exceeds the configured price cap ({Cap(MaxPromptPrice)} prompt / {Cap(MaxCompletionPrice)} completion, $ per million tokens).";
        return null;
    }

    private static string Cap(decimal price) => price > 0 ? price.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : "no limit";

    /// <summary>The models from <paramref name="models"/> this policy allows.</summary>
    public async Task<List<OpenRouterModel>> FilterAsync(IOpenRouterService openRouter, IEnumerable<OpenRouterModel> models, CancellationToken ct = default)
    {
        var zdr = await ZdrIdsAsync(openRouter, ct);
        return models.Where(m => Refusal(m.Id, m.Pricing, zdr) is null).ToList();
    }

    /// <summary>
    /// Why the model is refused, or null when it is allowed. A model missing
    /// from OpenRouter's catalogue has no price, so it is refused whenever a
    /// price cap is set.
    /// </summary>
    public async Task<string?> CheckModelAsync(IOpenRouterService openRouter, string modelId, CancellationToken ct = default)
    {
        var zdr = await ZdrIdsAsync(openRouter, ct);
        var pricing = IsPriceUnlimited
            ? null
            : (await openRouter.ListModelsAsync(ct)).FirstOrDefault(m => m.Id == modelId)?.Pricing;
        return Refusal(modelId, pricing, zdr);
    }

    private async Task<IReadOnlySet<string>> ZdrIdsAsync(IOpenRouterService openRouter, CancellationToken ct) =>
        ZdrOnly ? await openRouter.ListZdrModelIdsAsync(ct) : new HashSet<string>();
}
