using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// Which models a bot may use. The defaults are safe and cheap: only models
/// with a zero-data-retention endpoint, no <c>:batch</c> variants, and at most
/// 0.25 / 0.60 dollars per million prompt / completion tokens. A price of zero
/// on either side means that side is not capped. With reasoning turned off,
/// models that cannot stop reasoning are refused too.
/// </summary>
public record ModelPolicy(decimal MaxPromptPrice, decimal MaxCompletionPrice, bool ZdrOnly = false, bool ExcludeBatchModels = false, bool DisableReasoning = false)
{
    public const decimal DefaultMaxPromptPrice = 0.25m;
    public const decimal DefaultMaxCompletionPrice = 0.60m;

    public static ModelPolicy FromConfiguration(IConfiguration configuration) => new(
        Price(configuration, "OpenRouter:MaxPromptPrice", DefaultMaxPromptPrice),
        Price(configuration, "OpenRouter:MaxCompletionPrice", DefaultMaxCompletionPrice),
        configuration.GetValue("OpenRouter:ZdrOnly", true),
        configuration.GetValue("OpenRouter:ExcludeBatchModels", true),
        configuration.GetValue("OpenRouter:DisableReasoning", true));

    /// <summary>Unset (or blank, as an empty .env line gives) falls back to the default; an explicit 0 lifts the cap.</summary>
    private static decimal Price(IConfiguration configuration, string key, decimal fallback) =>
        string.IsNullOrWhiteSpace(configuration[key]) ? fallback : configuration.GetValue<decimal>(key);

    public bool IsPriceUnlimited => MaxPromptPrice <= 0 && MaxCompletionPrice <= 0;

    /// <summary>
    /// Why the model is not allowed, or null when it is. <paramref name="zdrModelIds"/>
    /// is only consulted under <see cref="ZdrOnly"/>.
    /// A model with no pricing, or variable pricing (OpenRouter reports -1 for
    /// routers such as openrouter/auto), cannot be checked, so it is refused
    /// whenever a price cap is set. <paramref name="reasoningMandatory"/> is
    /// whether the model always reasons, which only matters under
    /// <see cref="DisableReasoning"/>: such a model rejects the request to stop.
    /// </summary>
    public string? Refusal(string modelId, ModelPricing? pricing, IReadOnlySet<string> zdrModelIds, bool reasoningMandatory = false)
    {
        if (ExcludeBatchModels && modelId.EndsWith(":batch", StringComparison.Ordinal))
            return $"Model '{modelId}' is a batch variant, which cannot answer in a live chat.";
        if (ZdrOnly && !zdrModelIds.Contains(modelId))
            return $"Model '{modelId}' has no zero-data-retention endpoint on OpenRouter.";
        if (DisableReasoning && reasoningMandatory)
            return $"Model '{modelId}' always reasons, and reasoning is turned off (OpenRouter:DisableReasoning).";
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
        return models.Where(m => Refusal(m.Id, m.Pricing, zdr, m.ReasoningMandatory) is null).ToList();
    }

    /// <summary>
    /// Why the model is refused, or null when it is allowed. A model missing
    /// from OpenRouter's catalogue has no price, so it is refused whenever a
    /// price cap is set.
    /// </summary>
    public async Task<string?> CheckModelAsync(IOpenRouterService openRouter, string modelId, CancellationToken ct = default)
    {
        var zdr = await ZdrIdsAsync(openRouter, ct);
        var model = IsPriceUnlimited && !DisableReasoning
            ? null
            : (await openRouter.ListModelsAsync(ct)).FirstOrDefault(m => m.Id == modelId);
        return Refusal(modelId, model?.Pricing, zdr, model?.ReasoningMandatory ?? false);
    }

    /// <summary>
    /// 1 to 3 by prompt plus completion price: thirds of the combined cap, or
    /// fixed bands of 1 and 5 dollars per million tokens when a side is uncapped.
    /// A model without a fixed price is put in the top tier.
    /// </summary>
    public int PriceTier(ModelPricing? pricing)
    {
        if (pricing is null || pricing.Prompt < 0 || pricing.Completion < 0)
            return 3;

        var total = pricing.Prompt + pricing.Completion;
        var cap = MaxPromptPrice + MaxCompletionPrice;
        var (cheap, mid) = MaxPromptPrice > 0 && MaxCompletionPrice > 0 ? (cap / 3, cap * 2 / 3) : (1m, 5m);
        return total <= cheap ? 1 : total <= mid ? 2 : 3;
    }

    /// <summary>
    /// A lookup of whether a bot's model still works, built from one read of the
    /// catalogue. When the catalogue cannot be read (or comes back empty) every
    /// model counts as available: a flaky fetch must not silence every bot.
    /// </summary>
    public async Task<Func<string, ModelStatus>> StatusLookupAsync(IOpenRouterService openRouter, CancellationToken ct = default)
    {
        try
        {
            var models = await openRouter.ListModelsAsync(ct);
            if (models is not { Count: > 0 })
                return _ => ModelStatus.Ok;

            var zdr = await ZdrIdsAsync(openRouter, ct);
            var catalogue = new Dictionary<string, OpenRouterModel>(StringComparer.Ordinal);
            foreach (var model in models)
                catalogue.TryAdd(model.Id, model);
            return modelId => Status(modelId, catalogue, zdr);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return _ => ModelStatus.Ok;
        }
    }

    public ModelStatus Status(string modelId, IReadOnlyDictionary<string, OpenRouterModel> catalogue, IReadOnlySet<string> zdrModelIds)
    {
        if (!catalogue.TryGetValue(modelId, out var model))
            return new ModelStatus(ModelStatus.Missing, $"Model '{modelId}' is no longer offered on OpenRouter.");
        return Refusal(modelId, model.Pricing, zdrModelIds, model.ReasoningMandatory) is { } refusal
            ? new ModelStatus(ModelStatus.NotAllowed, refusal)
            : ModelStatus.Ok;
    }

    private async Task<IReadOnlySet<string>> ZdrIdsAsync(IOpenRouterService openRouter, CancellationToken ct) =>
        ZdrOnly ? await openRouter.ListZdrModelIdsAsync(ct) ?? new HashSet<string>() : new HashSet<string>();
}

/// <summary>Whether a bot's model can still be used, and why not when it cannot.</summary>
public record ModelStatus(string Status, string? Reason)
{
    public const string Available = "available";
    /// <summary>The id is no longer in OpenRouter's catalogue.</summary>
    public const string Missing = "missing";
    /// <summary>The model exists but <see cref="ModelPolicy"/> refuses it.</summary>
    public const string NotAllowed = "notAllowed";

    public static readonly ModelStatus Ok = new(Available, null);

    public bool IsAvailable => Status == Available;
}
