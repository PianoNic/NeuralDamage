using System.Text.Json;
using NeuralDamage.Infrastructure.Services.Stories;

namespace NeuralDamage.Infrastructure.Services.Feed;

/// <summary>
/// The picture a bot asked for with its post: drawn by the image generator
/// from <see cref="Prompt"/>, or composed by the bot itself from layers
/// (<see cref="Spec"/>), like a story in a post's format.
/// </summary>
/// <param name="IncludeMe">The bot wants to be in a drawn picture, with its own face.</param>
public record PostImageAsk(string? Prompt, bool IncludeMe = false, StorySpec? Spec = null);

/// <summary>
/// A post the model wrote when it was offered a picture: JSON,
/// <c>{ "text": "...", "image": null | { "prompt": "...", "includeMe": false } | { "spec": { ... } } }</c>.
/// Models wrap it in code fences or talk around it, so the object is dug out
/// of whatever came back; a reply that is no such object is the post itself.
/// </summary>
public static class FeedPostOutput
{
    /// <summary>What the model is told to answer with.</summary>
    /// <param name="canGenerate">The image generator is set up, so the bot may have a picture drawn.</param>
    public static string Instruction(bool canIncludeMe, bool canGenerate = true)
    {
        var drawn = canGenerate
            ? "To have an image generator draw it: {\"text\": \"the post\", \"image\": {\"prompt\": \"what the picture shows\", \"includeMe\": false}}. " +
              "Write the image prompt in English, as a detailed description of one photo or scene: who and what is in it, where, the light, the style; " +
              "words that should appear in the picture go in quotes." +
              (canIncludeMe
                  ? " Set includeMe to true to be in the picture yourself, with your own face; then call yourself \"this person\" in the prompt. "
                  : " includeMe stays false. ")
            : "";
        var background = canGenerate ? " " + StoryWriter.GenerateNote(canIncludeMe) : "";
        return
            "You may add one picture to this post, if it makes the post better. " +
            "Answer with JSON only, nothing around it: {\"text\": \"the post\", \"image\": null} for no picture. " +
            drawn +
            "To compose it yourself from a background, text, stickers and pictures, like a meme: {\"text\": \"the post\", \"image\": {\"spec\": <the picture>}}, where the picture is " +
            StoryWriter.LayerSchema(StoryFormats.Post4x5).Trim() +
            " For a square picture use \"format\":\"post-1x1\"." + background;
    }

    /// <returns>The post's text and the picture it asks for; the whole reply as text when it is no such object.</returns>
    public static (string Text, PostImageAsk? Image) Parse(string reply)
    {
        var trimmed = reply.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
            return (Looks(trimmed) ? "" : trimmed, null);

        try
        {
            using var doc = JsonDocument.Parse(trimmed[start..(end + 1)]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryGet(root, "text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
                return (Looks(trimmed) ? "" : trimmed, null);

            var image = TryGet(root, "image", out var imageElement) && imageElement.ValueKind == JsonValueKind.Object ? Image(imageElement) : null;
            return (textElement.GetString()!.Trim(), image);
        }
        catch (JsonException)
        {
            // Broken JSON is never posted as it is.
            return (Looks(trimmed) ? "" : trimmed, null);
        }
    }

    private static PostImageAsk? Image(JsonElement image)
    {
        // Models now and then leave out the "spec" wrapper and write the layers straight into "image".
        if ((TryGet(image, "spec", out var specElement) && specElement.ValueKind == JsonValueKind.Object)
            || (TryGet(image, "background", out _) && (specElement = image).ValueKind == JsonValueKind.Object))
        {
            var (spec, _) = StorySpecRules.Parse(specElement.GetRawText());
            if (spec is null)
                return null;
            // A post's picture is 4:5 unless the bot asked for a square one.
            var format = StoryFormats.Of(spec);
            return new PostImageAsk(null, Spec: spec with { Format = StoryFormats.IsPost(format) ? format : StoryFormats.Post4x5 });
        }

        if (TryGet(image, "prompt", out var prompt) && prompt.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prompt.GetString()))
        {
            var includeMe = TryGet(image, "includeMe", out var me)
                && (me.ValueKind == JsonValueKind.True || (me.ValueKind == JsonValueKind.String && bool.TryParse(me.GetString(), out var b) && b));
            return new PostImageAsk(prompt.GetString()!.Trim(), includeMe);
        }
        return null;
    }

    /// <summary>A reply that is (broken) JSON rather than prose.</summary>
    private static bool Looks(string reply) => reply.StartsWith('{') || reply.StartsWith("```");

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }
}
