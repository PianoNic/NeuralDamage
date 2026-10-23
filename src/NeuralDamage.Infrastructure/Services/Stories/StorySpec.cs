using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NeuralDamage.Infrastructure.Services.Stories;

/// <summary>
/// A story as JSON: a background, then layers drawn in order. Positions are
/// percentages of the story (x across, y down, the layer's centre); sizes are
/// in points of a 360-wide story, so 1 point is 3 pixels of the 1080×1920 PNG.
/// People build it in the editor and bots write it; the server checks it with
/// <see cref="StorySpecRules.Validate"/> before it renders it.
/// </summary>
/// <example>
/// { "background": { "color": "#1f3a2b", "image": "upload:…", "fit": "cover" },
///   "layers": [ { "type": "text", "text": "Hi", "x": 50, "y": 30, "size": 28, "color": "#fafafa", "weight": 700 },
///               { "type": "sticker", "name": "swole-doge", "x": 50, "y": 70, "size": 130, "rotation": -8 },
///               { "type": "image", "image": "wiki:Eiffel Tower", "x": 50, "y": 50, "size": 200 } ] }
/// </example>
public record StorySpec(StoryBackground Background, List<StoryLayer> Layers);

/// <param name="Color">"#rrggbb"; shows under the image, and while it loads.</param>
/// <param name="Image">An image reference (<see cref="StoryImageRef"/>), or null for a plain colour.</param>
/// <param name="Fit">Only "cover": the image fills the story, centred, and what sticks out is cut.</param>
public record StoryBackground(string Color, string? Image = null, string? Fit = null);

/// <param name="Type">text, sticker or image.</param>
/// <param name="Name">A sticker's library name.</param>
/// <param name="Image">An image layer's reference (<see cref="StoryImageRef"/>).</param>
/// <param name="Rotation">Degrees clockwise, for stickers and images.</param>
public record StoryLayer(
    string Type,
    double X = 50,
    double Y = 50,
    double Size = 24,
    string? Text = null,
    string? Color = null,
    int? Weight = null,
    string? Name = null,
    string? Image = null,
    double? Rotation = null);

/// <summary>
/// Where a story's picture comes from: <c>upload:&lt;id&gt;</c>, a picture the
/// author uploaded for it, or <c>wiki:&lt;name&gt;</c> (optionally <c>#1</c> or
/// <c>#2</c> for the second or third), a picture from the Wikimedia lookup
/// new bots get theirs from.
/// </summary>
public abstract record StoryImageRef
{
    public sealed record Upload(Guid Id) : StoryImageRef;
    public sealed record Wiki(string Name, int Index) : StoryImageRef;

    public const int MaxWikiName = 80;

    public static StoryImageRef? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.StartsWith("upload:", StringComparison.OrdinalIgnoreCase))
            return Guid.TryParse(value[7..], out var id) ? new Upload(id) : null;
        if (value.StartsWith("wiki:", StringComparison.OrdinalIgnoreCase))
        {
            var name = value[5..].Trim();
            var index = 0;
            var hash = name.LastIndexOf('#');
            if (hash > 0 && int.TryParse(name[(hash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var i))
                (name, index) = (name[..hash].Trim(), i);
            return name.Length is > 0 and <= MaxWikiName && index is >= 0 and < 3 ? new Wiki(name, index) : null;
        }
        return null;
    }
}

/// <summary>Reading a spec and the limits it has to keep.</summary>
public static partial class StorySpecRules
{
    public const int MaxLayers = 12;
    public const int MaxImageLayers = 3;
    public const int MaxTextLength = 200;
    public const int MaxTotalText = 600;
    public const double MinTextSize = 10, MaxTextSize = 120;
    public const double MinPictureSize = 30, MaxPictureSize = 360;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>The spec in <paramref name="json"/>, or null with the reason when it is not one.</summary>
    public static (StorySpec? Spec, string? Error) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, "The story is empty.");
        try
        {
            var spec = JsonSerializer.Deserialize<StorySpec>(json, Json);
            return spec?.Background is null ? (null, "The story needs a background.") : (spec with { Layers = spec.Layers ?? [] }, null);
        }
        catch (JsonException)
        {
            return (null, "The story is not valid JSON.");
        }
    }

    public static string Serialize(StorySpec spec) => JsonSerializer.Serialize(spec, Json);

    /// <summary>
    /// Every way <paramref name="spec"/> breaks the rules: known stickers only,
    /// colours as "#rrggbb", sizes, positions and rotations in bounds, limits on
    /// text and layers, and image references that parse. Empty when it is fine.
    /// </summary>
    public static List<string> Validate(StorySpec spec, Func<string, bool> stickerExists)
    {
        var errors = new List<string>();
        if (!IsColor(spec.Background.Color))
            errors.Add("The background colour must look like #1f3a2b.");
        if (spec.Background.Image is { } bg && StoryImageRef.Parse(bg) is null)
            errors.Add("The background image is not one this app knows.");
        if (spec.Background.Fit is { } fit && fit != "cover")
            errors.Add("A background image can only cover the story.");

        if (spec.Layers.Count == 0 && spec.Background.Image is null)
            errors.Add("Add some text, a sticker or a picture.");
        if (spec.Layers.Count > MaxLayers)
            errors.Add($"A story can have at most {MaxLayers} layers.");
        if (spec.Layers.Count(l => l.Type == "image") > MaxImageLayers)
            errors.Add($"A story can have at most {MaxImageLayers} pictures.");
        if (spec.Layers.Sum(l => l.Text?.Length ?? 0) > MaxTotalText)
            errors.Add($"A story can hold at most {MaxTotalText} characters of text.");

        foreach (var (layer, i) in spec.Layers.Select((l, i) => (l, i + 1)))
        {
            var where = $"Layer {i}";
            if (layer.X is < 0 or > 100 || layer.Y is < 0 or > 100 || double.IsNaN(layer.X) || double.IsNaN(layer.Y))
                errors.Add($"{where} is off the story.");
            if (layer.Rotation is { } r && (r is < -180 or > 180 || double.IsNaN(r)))
                errors.Add($"{where} turns too far.");
            switch (layer.Type)
            {
                case "text":
                    if (string.IsNullOrWhiteSpace(layer.Text))
                        errors.Add($"{where} has no text.");
                    else if (layer.Text.Length > MaxTextLength)
                        errors.Add($"{where} is longer than {MaxTextLength} characters.");
                    if (layer.Size is < MinTextSize or > MaxTextSize || double.IsNaN(layer.Size))
                        errors.Add($"{where}'s text size must be {MinTextSize} to {MaxTextSize}.");
                    if (layer.Color is { } color && !IsColor(color))
                        errors.Add($"{where}'s colour must look like #fafafa.");
                    if (layer.Weight is { } weight && weight is < 100 or > 900)
                        errors.Add($"{where}'s weight must be 100 to 900.");
                    break;
                case "sticker":
                    if (string.IsNullOrWhiteSpace(layer.Name) || !stickerExists(layer.Name.Trim()))
                        errors.Add($"{where}: there is no sticker called \"{layer.Name}\".");
                    if (layer.Size is < MinPictureSize or > MaxPictureSize || double.IsNaN(layer.Size))
                        errors.Add($"{where}'s size must be {MinPictureSize} to {MaxPictureSize}.");
                    break;
                case "image":
                    if (StoryImageRef.Parse(layer.Image) is null)
                        errors.Add($"{where}'s picture is not one this app knows.");
                    if (layer.Size is < MinPictureSize or > MaxPictureSize || double.IsNaN(layer.Size))
                        errors.Add($"{where}'s size must be {MinPictureSize} to {MaxPictureSize}.");
                    break;
                default:
                    errors.Add($"{where} is a \"{layer.Type}\", which is not text, sticker or image.");
                    break;
            }
        }
        return errors;
    }

    /// <summary>"#rgb" or "#rrggbb".</summary>
    public static bool IsColor(string? value) => value is not null && Color().IsMatch(value);

    /// <summary>Every picture the spec refers to, background first.</summary>
    public static IEnumerable<StoryImageRef> Images(StorySpec spec) =>
        new[] { spec.Background.Image }.Concat(spec.Layers.Where(l => l.Type == "image").Select(l => l.Image))
            .Select(StoryImageRef.Parse)
            .OfType<StoryImageRef>();

    /// <summary>What the story says, for whoever cannot see it: its text and stickers, in order.</summary>
    public static string Describe(StorySpec spec) =>
        string.Join(" / ", spec.Layers.Select(l => l.Type switch
        {
            "text" => l.Text?.Trim(),
            "sticker" => $"[sticker {l.Name}]",
            "image" => "[picture]",
            _ => null,
        }).Where(s => !string.IsNullOrWhiteSpace(s)));

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex Color();
}
