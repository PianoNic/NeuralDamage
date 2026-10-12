using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// A model the picker suggests, with a short "choose this when..." line.
/// Configured as <c>Recommendations:N:Model</c> and <c>Recommendations:N:Hint</c>,
/// so the list can change without a release.
/// </summary>
public record ModelRecommendation(string Model, string Hint)
{
    /// <summary>The <c>Recommendations</c> entries in order; ones without a model are skipped.</summary>
    public static List<ModelRecommendation> FromConfiguration(IConfiguration configuration) =>
        configuration.GetSection("Recommendations").GetChildren()
            .OrderBy(section => int.TryParse(section.Key, out var index) ? index : int.MaxValue)
            .Select(section => new ModelRecommendation(section["Model"]?.Trim() ?? "", section["Hint"]?.Trim() ?? ""))
            .Where(recommendation => recommendation.Model.Length > 0)
            .ToList();

    /// <summary>
    /// <paramref name="offered"/> with the recommended ones ranked and given their hint.
    /// The default model comes first, recommended or not; the rest keep the configured
    /// order. A recommendation that is not offered (gone, or refused by the policy) is
    /// left out silently.
    /// </summary>
    public static List<OpenRouterModel> Apply(IReadOnlyList<ModelRecommendation> recommendations, IEnumerable<OpenRouterModel> offered, string? defaultModel)
    {
        var hints = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        if (!string.IsNullOrWhiteSpace(defaultModel))
            order.Add(defaultModel);
        foreach (var recommendation in recommendations)
        {
            hints.TryAdd(recommendation.Model, recommendation.Hint);
            if (!order.Contains(recommendation.Model))
                order.Add(recommendation.Model);
        }

        var models = offered.ToList();
        var offeredIds = models.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var ranks = order.Where(offeredIds.Contains)
            .Select((id, rank) => (id, rank))
            .ToDictionary(x => x.id, x => x.rank, StringComparer.Ordinal);

        return models
            .Select(m => ranks.TryGetValue(m.Id, out var rank)
                ? m with { RecommendationRank = rank, RecommendationHint = hints.GetValueOrDefault(m.Id) is { Length: > 0 } hint ? hint : null }
                : m)
            .ToList();
    }
}
