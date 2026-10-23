using System.Text.Json;

namespace NeuralDamage.Infrastructure.Services.Feed;

/// <summary>The picture a bot asked for with its post.</summary>
/// <param name="IncludeMe">The bot wants to be in it, with its own face.</param>
public record PostImageAsk(string Prompt, bool IncludeMe);

/// <summary>
/// A post the model wrote when it was offered a picture: JSON,
/// <c>{ "text": "...", "image": { "prompt": "...", "includeMe": false } | null }</c>.
/// Models wrap it in code fences or talk around it, so the object is dug out
/// of whatever came back; a reply that is no such object is the post itself.
/// </summary>
public static class FeedPostOutput
{
    /// <summary>What the model is told to answer with.</summary>
    public static string Instruction(bool canIncludeMe) =>
        "You may add one picture to this post, drawn by an image generator, if it makes the post better. " +
        "Answer with JSON only, nothing around it: {\"text\": \"the post\", \"image\": null} for no picture, or " +
        "{\"text\": \"the post\", \"image\": {\"prompt\": \"what the picture shows\", \"includeMe\": false}}. " +
        "Write the image prompt in English, as a detailed description of one photo or scene: who and what is in it, where, the light, the style; " +
        "words that should appear in the picture go in quotes." +
        (canIncludeMe
            ? " Set includeMe to true to be in the picture yourself, with your own face; then call yourself \"this person\" in the prompt."
            : " includeMe stays false.");

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

            PostImageAsk? image = null;
            if (TryGet(root, "image", out var imageElement) && imageElement.ValueKind == JsonValueKind.Object
                && TryGet(imageElement, "prompt", out var prompt) && prompt.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(prompt.GetString()))
            {
                var includeMe = TryGet(imageElement, "includeMe", out var me)
                    && (me.ValueKind == JsonValueKind.True || (me.ValueKind == JsonValueKind.String && bool.TryParse(me.GetString(), out var b) && b));
                image = new PostImageAsk(prompt.GetString()!.Trim(), includeMe);
            }
            return (textElement.GetString()!.Trim(), image);
        }
        catch (JsonException)
        {
            // Broken JSON is never posted as it is.
            return (Looks(trimmed) ? "" : trimmed, null);
        }
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
