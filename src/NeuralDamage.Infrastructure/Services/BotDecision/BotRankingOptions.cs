using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

/// <summary>
/// Settings for Jev, which decides what every bot does with a message, read
/// from the <c>BotRanking</c> section. Everything but the key has a default,
/// and the key falls back to the OpenRouter key because the Decisions API sits
/// on the same account.
/// </summary>
public record BotRankingOptions
{
    public const string DefaultEndpoint = "https://openrouter.ai/api/alpha/decisions";
    public const string DefaultModel = "~typesafe/jev-latest";

    /// <summary>The emoji each react option puts on the message.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultEmojis = new Dictionary<string, string>
    {
        [BotDecisionEngine.ReactLaugh] = "😂",
        [BotDecisionEngine.ReactLove] = "❤️",
        [BotDecisionEngine.ReactWow] = "😮",
        [BotDecisionEngine.ReactThumbs] = "👍",
    };

    public string Endpoint { get; init; } = DefaultEndpoint;
    public string Model { get; init; } = DefaultModel;
    public string? ApiKey { get; init; }

    /// <summary>Lowest probability of <c>reply</c>, when Jev chose it, for the bot to write.</summary>
    public double ReplyThreshold { get; init; } = 0.6;

    /// <summary>Lowest probability of the chosen react option for the bot to react.</summary>
    public double ReactThreshold { get; init; } = 0.5;

    /// <summary>
    /// Safety cap on how many bots reply to one message, the likeliest first.
    /// It guards cost in chats with many bots; normal use never reaches it.
    /// </summary>
    public int MaxReplies { get; init; } = 5;

    /// <summary>
    /// From this conversation health score (0 healthy, 2 spiralling) the bots
    /// hold back when answering each other: only <see cref="CautiousMaxReplies"/>
    /// of them, and only at <see cref="CautiousReplyThreshold"/> or above.
    /// </summary>
    public double CautiousHealth { get; init; } = 1.0;

    /// <summary>From this conversation health score bots stop answering each other and only react.</summary>
    public double SilentHealth { get; init; } = 1.5;

    public double CautiousReplyThreshold { get; init; } = 0.85;
    public int CautiousMaxReplies { get; init; } = 1;

    /// <summary>A person who wrote within this long counts as active in the chat, for Jev's <c>flow</c>.</summary>
    public TimeSpan HumansActiveWindow { get; init; } = TimeSpan.FromMinutes(5);

    public IReadOnlyDictionary<string, string> Emojis { get; init; } = DefaultEmojis;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public static BotRankingOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("BotRanking");
        var defaults = new BotRankingOptions();

        // Only the four react options exist in the question, so only their
        // emoji can be changed.
        var emojis = DefaultEmojis.ToDictionary(e => e.Key, e => NullIfBlank(section[$"Emojis:{e.Key}"]) ?? e.Value);

        return new BotRankingOptions
        {
            Endpoint = NullIfBlank(section["Endpoint"]) ?? DefaultEndpoint,
            Model = NullIfBlank(section["Model"]) ?? DefaultModel,
            ApiKey = NullIfBlank(section["ApiKey"]) ?? NullIfBlank(configuration["OpenRouter:ApiKey"]),
            ReplyThreshold = section.GetValue<double?>("ReplyThreshold") ?? defaults.ReplyThreshold,
            ReactThreshold = section.GetValue<double?>("ReactThreshold") ?? defaults.ReactThreshold,
            MaxReplies = section.GetValue<int?>("MaxReplies") ?? defaults.MaxReplies,
            CautiousHealth = section.GetValue<double?>("CautiousHealth") ?? defaults.CautiousHealth,
            SilentHealth = section.GetValue<double?>("SilentHealth") ?? defaults.SilentHealth,
            CautiousReplyThreshold = section.GetValue<double?>("CautiousReplyThreshold") ?? defaults.CautiousReplyThreshold,
            CautiousMaxReplies = section.GetValue<int?>("CautiousMaxReplies") ?? defaults.CautiousMaxReplies,
            HumansActiveWindow = section.GetValue<double?>("HumansActiveMinutes") is { } minutes ? TimeSpan.FromMinutes(minutes) : defaults.HumansActiveWindow,
            Emojis = emojis,
            Timeout = section.GetValue<double?>("TimeoutSeconds") is { } seconds ? TimeSpan.FromSeconds(seconds) : defaults.Timeout,
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
