namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// How much of a chat a bot reads raw, and how the rest is kept as a running
/// summary. Bound from the "History" section.
/// </summary>
public class HistoryOptions
{
    public const string SectionName = "History";

    /// <summary>The share of the model's context the raw history may take.</summary>
    public double ContextShare { get; set; } = 0.25;

    /// <summary>
    /// The most tokens of raw history, whatever the context: a model with a
    /// million tokens would otherwise be sent huge prompts on every reply.
    /// </summary>
    public int MaxTokens { get; set; } = 32_000;

    /// <summary>The least characters of raw history, which is what every bot got before the budget followed the model.</summary>
    public int MinChars { get; set; } = 12_000;

    /// <summary>A summary longer than this is compacted again.</summary>
    public int SummaryMaxChars { get; set; } = 4_000;

    /// <summary>
    /// The most characters of dropped messages folded into the summary at
    /// once, newest first. Anything older in the same fold is skipped, which
    /// only matters for a chat that was long before summaries existed.
    /// </summary>
    public int MaxFoldChars { get; set; } = 60_000;

    /// <summary>The model that writes the summary; empty means <c>App:DefaultModel</c>.</summary>
    public string? SummaryModel { get; set; }
}

/// <summary>How much raw history a bot reads, in characters and messages.</summary>
public record HistoryBudget(int Chars)
{
    /// <summary>The budget every bot had before it followed the model: 12k characters, 50 messages.</summary>
    public static readonly HistoryBudget Default = new(12_000);

    /// <summary>Characters per token, roughly, for English chat text.</summary>
    public const int CharsPerToken = 4;

    /// <summary>The message cap scales with the characters, at the old ratio of 50 messages to 12k.</summary>
    private const int CharsPerMessage = 240;

    public int Messages => Math.Max(1, Chars / CharsPerMessage);

    /// <summary>
    /// A share of <paramref name="contextTokens"/>, capped at
    /// <see cref="HistoryOptions.MaxTokens"/> and never below
    /// <see cref="HistoryOptions.MinChars"/>.
    /// </summary>
    public static HistoryBudget For(int? contextTokens, HistoryOptions options)
    {
        var tokens = Math.Min(Math.Max(0, contextTokens ?? 0) * options.ContextShare, options.MaxTokens);
        return new HistoryBudget(Math.Max((int)(tokens * CharsPerToken), Math.Max(1, options.MinChars)));
    }
}
