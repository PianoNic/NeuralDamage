using Microsoft.Extensions.Configuration;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// An OpenAI-compatible endpoint next to OpenRouter, configured under
/// <c>Providers:&lt;Name&gt;</c>. Its models are addressed as <c>&lt;Name&gt;:&lt;model&gt;</c>,
/// e.g. <c>gateway:sgl/deepseek</c>. <see cref="Models"/> is what it offers;
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

    /// <summary>Every <c>Providers:*</c> section with a BaseUrl. Models is a comma-separated list (or an array).</summary>
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
