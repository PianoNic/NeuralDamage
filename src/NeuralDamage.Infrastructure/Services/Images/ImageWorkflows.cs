using System.Text.Json.Nodes;

namespace NeuralDamage.Infrastructure.Services.Images;

/// <summary>What one picture is made from.</summary>
/// <param name="Workflow"><see cref="ImageWorkflows.Scene"/> or <see cref="ImageWorkflows.Self"/>.</param>
/// <param name="References">
/// For the "self" workflow: one to <see cref="ImageWorkflows.MaxReferences"/>
/// padded pictures of the person, the profile picture first.
/// </param>
public record RenderRequest(string Workflow, string Prompt, int Width, int Height, long Seed, IReadOnlyList<byte[]>? References = null);

/// <summary>
/// The ComfyUI workflows the app ships (API format, embedded as resources) and
/// how a request's parameters go into them: by node title (<c>_meta.title</c>),
/// so the graphs can be re-exported from ComfyUI without touching the code.
/// </summary>
public static class ImageWorkflows
{
    /// <summary>Z-Image Turbo: about 15 s, good at text inside the picture.</summary>
    public const string Scene = "scene";

    /// <summary>Qwen-Image-Edit 2511 with a bot's picture as the reference: about 75 s, keeps the face.</summary>
    public const string Self = "self";

    /// <summary>The most pictures the "self" workflow takes: Qwen-Image-Edit's <c>image1</c> to <c>image3</c>.</summary>
    public const int MaxReferences = 3;

    /// <summary>The graph for <paramref name="request"/>, with the uploaded references' names when it has them.</summary>
    public static JsonObject Build(RenderRequest request, ImageGenOptions options, IReadOnlyList<string>? referenceNames = null)
    {
        var graph = Load(request.Workflow);
        Set(graph, "Prompt", PromptKey(graph), request.Prompt);
        Set(graph, "Size", "width", request.Width);
        Set(graph, "Size", "height", request.Height);
        Set(graph, "Sampler", "seed", request.Seed);

        if (request.Workflow == Self)
        {
            Set(graph, "Model", "unet_name", options.SelfModel);
            Set(graph, "Text encoder", "clip_name", options.SelfTextEncoder);
            Set(graph, "VAE", "vae_name", options.SelfVae);
            Set(graph, "LoRA", "lora_name", options.SelfLora);
            if (referenceNames is not { Count: > 0 and <= MaxReferences })
                throw new ArgumentException($"The self workflow needs one to {MaxReferences} reference images.", nameof(referenceNames));
            Set(graph, "Reference", "image", referenceNames[0]);
            for (var i = 1; i < referenceNames.Count; i++)
                AddReference(graph, referenceNames[i], $"image{i + 1}");
        }
        else
        {
            Set(graph, "Model", "unet_name", options.SceneModel);
            Set(graph, "Text encoder", "clip_name", options.SceneTextEncoder);
            Set(graph, "VAE", "vae_name", options.SceneVae);
        }
        return graph;
    }

    /// <summary>
    /// One more picture for the "self" workflow: a copy of the first
    /// reference's load and scale nodes, loading <paramref name="name"/>, fed
    /// into every text encoder as <paramref name="slot"/> (<c>image2</c>, <c>image3</c>).
    /// </summary>
    private static void AddReference(JsonObject graph, string name, string slot)
    {
        var encoders = graph.Select(n => n.Value as JsonObject)
            .Where(n => (string?)n?["class_type"] == "TextEncodeQwenImageEditPlus")
            .Select(n => n!["inputs"]!.AsObject())
            .ToList();
        var scaleId = (string)Node(graph, "Prompt")["inputs"]!["image1"]![0]!;
        var nextId = graph.Select(n => int.TryParse(n.Key, out var id) ? id : 0).Max() + 1;
        var loadId = nextId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var newScaleId = (nextId + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var load = Node(graph, "Reference").DeepClone().AsObject();
        load["_meta"] = new JsonObject { ["title"] = $"Reference {slot[^1]}" };
        load["inputs"]!["image"] = name;
        var scale = graph[scaleId]!.DeepClone().AsObject();
        scale["inputs"]!["image"] = new JsonArray(loadId, 0);
        graph[loadId] = load;
        graph[newScaleId] = scale;
        foreach (var inputs in encoders)
            inputs[slot] = new JsonArray(newScaleId, 0);
    }

    /// <summary>
    /// The prompt for the "self" workflow: the scene, that the face is the
    /// references', and what the person looks like when that is written down.
    /// </summary>
    public static string SelfPrompt(string scene, int references = 1, string? looks = null)
    {
        var who = references > 1
            ? "The person from the reference images (all of the same person) is in this picture"
            : "The person from the reference image is in this picture";
        var prompt = $"{scene.Trim().TrimEnd('.')}. {who}: keep their face and identity exactly the same.";
        return string.IsNullOrWhiteSpace(looks) ? prompt : $"{prompt} How they look: {looks.Trim()}";
    }

    public static JsonObject Load(string workflow)
    {
        if (workflow is not (Scene or Self))
            throw new ArgumentException($"No workflow named {workflow}.", nameof(workflow));
        using var stream = typeof(ImageWorkflows).Assembly.GetManifestResourceStream($"NeuralDamage.Infrastructure.Workflows.{workflow}.json")
            ?? throw new InvalidOperationException($"The {workflow} workflow is not embedded.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    /// <summary>The node titled <paramref name="title"/>; a workflow without it is broken.</summary>
    public static JsonObject Node(JsonObject graph, string title) =>
        graph.Select(n => n.Value as JsonObject).FirstOrDefault(n => (string?)n?["_meta"]?["title"] == title)
        ?? throw new InvalidOperationException($"The workflow has no node titled \"{title}\".");

    private static string PromptKey(JsonObject graph) =>
        Node(graph, "Prompt")["inputs"]!.AsObject().ContainsKey("text") ? "text" : "prompt";

    private static void Set(JsonObject graph, string title, string input, JsonNode? value) =>
        Node(graph, title)["inputs"]![input] = value;
}
