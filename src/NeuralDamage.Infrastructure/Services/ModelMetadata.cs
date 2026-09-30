using System.Globalization;
using System.Text.RegularExpressions;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// What the model browser shows about a model, derived from OpenRouter's
/// catalogue entry so the UI does not have to guess.
/// </summary>
public static partial class ModelMetadata
{
    public const string Reasoning = "reasoning";
    public const string Vision = "vision";
    public const string Code = "code";
    public const string Fast = "fast";

    private static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ai21"] = "AI21",
        ["amazon"] = "Amazon",
        ["anthropic"] = "Anthropic",
        ["cohere"] = "Cohere",
        ["deepseek"] = "DeepSeek",
        ["google"] = "Google",
        ["ibm-granite"] = "IBM",
        ["meta-llama"] = "Meta",
        ["microsoft"] = "Microsoft",
        ["minimax"] = "MiniMax",
        ["mistralai"] = "Mistral",
        ["moonshotai"] = "Moonshot AI",
        ["nousresearch"] = "Nous Research",
        ["nvidia"] = "NVIDIA",
        ["openai"] = "OpenAI",
        ["openrouter"] = "OpenRouter",
        ["perplexity"] = "Perplexity",
        ["qwen"] = "Qwen",
        ["x-ai"] = "xAI",
        ["z-ai"] = "Z.ai",
    };

    /// <summary>The vendor's display name, from the part of the id before the slash.</summary>
    public static string Provider(string modelId)
    {
        var slash = modelId.IndexOf('/');
        var prefix = slash < 0 ? modelId : modelId[..slash];
        if (Vendors.TryGetValue(prefix, out var vendor))
            return vendor;

        var words = prefix.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
    }

    /// <summary>The first sentence of OpenRouter's description, with markdown links reduced to their text.</summary>
    public static string? Summary(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var text = Whitespace().Replace(MarkdownLink().Replace(description, "$1"), " ").Trim();
        var end = SentenceEnd().Match(text);
        return end.Success ? text[..end.Index] : text;
    }

    /// <remarks>
    /// Reasoning and vision come from what OpenRouter reports; code and fast are
    /// guessed from the name, since the catalogue has no field for them.
    /// </remarks>
    public static IReadOnlyList<string> Capabilities(string id, string name, IEnumerable<string>? supportedParameters = null, IEnumerable<string>? inputModalities = null)
    {
        var capabilities = new List<string>();
        var parameters = supportedParameters?.ToList() ?? [];
        if (parameters.Contains("reasoning") || parameters.Contains("include_reasoning"))
            capabilities.Add(Reasoning);
        if (inputModalities?.Contains("image") == true)
            capabilities.Add(Vision);
        if (ContainsAny(id, "code", "coder") || ContainsAny(name, "code", "coder"))
            capabilities.Add(Code);
        if (ContainsAny(id, "flash", "lite", "mini", "nano", "haiku", "turbo"))
            capabilities.Add(Fast);
        return capabilities;
    }

    private static bool ContainsAny(string text, params string[] words) =>
        words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // The punctuation stays with the sentence; "e.g." style abbreviations can
    // cut it short, which is acceptable for a one-line blurb.
    [GeneratedRegex(@"(?<=[.!?])\s")]
    private static partial Regex SentenceEnd();
}
