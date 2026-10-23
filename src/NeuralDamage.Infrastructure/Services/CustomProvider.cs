using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// An OpenAI-compatible endpoint next to OpenRouter, configured under
/// <c>Providers:&lt;Name&gt;</c>. Its models are addressed as <c>&lt;Name&gt;:&lt;model&gt;</c>,
/// e.g. <c>vllm:deepseek-chat</c>. <see cref="Models"/> is what it offers;
/// empty means whatever its <c>/models</c> lists.
/// </summary>
public record CustomProvider(string Name, string BaseUrl, string ApiKey, IReadOnlyList<string> Models)
{
    /// <summary>
    /// <c>Providers:&lt;Name&gt;:ContextLength</c>: the context of its models, in
    /// tokens. It wins over what <c>/models</c> says, and saves a probe when
    /// that says nothing.
    /// </summary>
    public int? ContextLength { get; init; }

    /// <summary>
    /// <c>Providers:&lt;Name&gt;:DisplayName</c>, what the picker calls it ("vLLM"
    /// for <c>vllm</c>); <see cref="Name"/> when unset. Model ids keep <see cref="Name"/>.
    /// </summary>
    public string? DisplayName { get; init; }

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

    /// <summary>
    /// <c>Providers:&lt;Name&gt;:ModelNames</c>: readable names by model id, e.g.
    /// <c>my-model</c> to "My Model". Ids stay as they are.
    /// </summary>
    public IReadOnlyDictionary<string, string> ModelNames { get; init; } = new Dictionary<string, string>();

    /// <summary>What <paramref name="model"/> is called: its configured name, or the id itself.</summary>
    public string ModelName(string model) => ModelNames.TryGetValue(model, out var name) ? name : model;

    /// <summary>
    /// <c>id=Display name</c> pairs separated by <c>;</c>. The first <c>=</c> splits
    /// a pair, so a name may contain one; a pair without an id or a name is skipped,
    /// and a later pair for the same id wins.
    /// </summary>
    public static Dictionary<string, string> ParseModelNames(string? value)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in (value ?? "").Split(';'))
        {
            var equals = pair.IndexOf('=');
            if (equals < 0)
                continue;
            var (id, name) = (pair[..equals].Trim(), pair[(equals + 1)..].Trim());
            if (id.Length > 0 && name.Length > 0)
                names[id] = name;
        }
        return names;
    }

    /// <summary>
    /// The configured name of a custom <paramref name="modelId"/>
    /// (<c>&lt;Name&gt;:&lt;model&gt;</c>), even when its provider no longer offers it;
    /// the id itself otherwise.
    /// </summary>
    public static string NameOf(IConfiguration? configuration, string modelId) =>
        configuration is not null && Split(modelId) is { } split
            && FromConfiguration(configuration).FirstOrDefault(p => string.Equals(p.Name, split.Provider, StringComparison.OrdinalIgnoreCase)) is { } provider
            && provider.ModelNames.TryGetValue(split.Model, out var name)
            ? name
            : modelId;

    /// <summary>Every <c>Providers:*</c> section with a BaseUrl. Models is a comma-separated list (or an array), ModelNames <c>id=name;id=name</c>.</summary>
    public static List<CustomProvider> FromConfiguration(IConfiguration configuration) =>
        configuration.GetSection("Providers").GetChildren()
            .Where(section => !string.IsNullOrWhiteSpace(section["BaseUrl"]))
            .Select(section => new CustomProvider(
                section.Key,
                section["BaseUrl"]!.Trim().TrimEnd('/'),
                section["ApiKey"] ?? "",
                (section["Models"]?.Split(',') ?? section.GetSection("Models").GetChildren().Select(c => c.Value ?? "").ToArray())
                    .Select(m => m.Trim())
                    .Where(m => m.Length > 0)
                    .ToList())
            {
                ContextLength = int.TryParse(section["ContextLength"], out var length) && length > 0 ? length : null,
                DisplayName = section["DisplayName"]?.Trim() is { Length: > 0 } displayName ? displayName : null,
                ModelNames = ParseModelNames(section["ModelNames"]),
            })
            .ToList();

    /// <summary>
    /// The provider and model of a <c>&lt;Name&gt;:&lt;model&gt;</c> id, or null for an
    /// OpenRouter id. OpenRouter ids always start with <c>vendor/</c> and any
    /// colon in them (<c>:free</c>, <c>:batch</c>) comes after the slash, so a
    /// colon before the first slash, or without one, marks a custom model.
    /// </summary>
    public static (string Provider, string Model)? Split(string modelId)
    {
        var colon = modelId.IndexOf(':');
        if (colon <= 0 || colon == modelId.Length - 1)
            return null;
        var slash = modelId.IndexOf('/');
        return slash >= 0 && slash < colon ? null : (modelId[..colon], modelId[(colon + 1)..]);
    }

    public static bool IsCustomModelId(string modelId) => Split(modelId) is not null;

    /// <summary>The id a bot stores for <paramref name="model"/> of this provider.</summary>
    public string ModelId(string model) => $"{Name}:{model}";

    /// <summary>
    /// The model a new bot starts on: <paramref name="configured"/> (<c>App:DefaultModel</c>)
    /// when it is offered, otherwise the first custom model, otherwise none.
    /// </summary>
    public static string? DefaultModel(string? configured, IReadOnlyList<OpenRouterModel> offered)
    {
        if (!string.IsNullOrWhiteSpace(configured) && offered.Any(m => m.Id == configured.Trim()))
            return configured.Trim();
        return offered.FirstOrDefault(m => m.IsCustom)?.Id;
    }
}
