using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

/// <summary>
/// Settings for the Tier 3 judge, read from the <c>BotRanking</c> section.
/// Everything but the key has a default, and the key falls back to the
/// OpenRouter key because the Decisions API sits on the same account.
/// </summary>
public record BotRankingOptions
{
    public const string DefaultEndpoint = "https://openrouter.ai/api/alpha/decisions";
    public const string DefaultModel = "~typesafe/jev-latest";

    public string Endpoint { get; init; } = DefaultEndpoint;
    public string Model { get; init; } = DefaultModel;
    public string? ApiKey { get; init; }

    /// <summary>Minimum probability for a bot to reply.</summary>
    public double Threshold { get; init; } = 0.6;

    /// <summary>Most bots Tier 3 lets reply to a single message.</summary>
    public int MaxResponders { get; init; } = 2;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public static BotRankingOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("BotRanking");
        var apiKey = section["ApiKey"];

        return new BotRankingOptions
        {
            Endpoint = NullIfBlank(section["Endpoint"]) ?? DefaultEndpoint,
            Model = NullIfBlank(section["Model"]) ?? DefaultModel,
            ApiKey = NullIfBlank(apiKey) ?? NullIfBlank(configuration["OpenRouter:ApiKey"]),
            Threshold = section.GetValue<double?>("Threshold") ?? 0.6,
            MaxResponders = section.GetValue<int?>("MaxResponders") ?? 2,
            Timeout = TimeSpan.FromSeconds(section.GetValue<double?>("TimeoutSeconds") ?? 5),
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
