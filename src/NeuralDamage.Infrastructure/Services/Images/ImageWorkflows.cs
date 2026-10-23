using System.Text.Json.Nodes;

namespace NeuralDamage.Infrastructure.Services.Images;

/// <summary>What one picture is made from.</summary>
/// <param name="Workflow"><see cref="ImageWorkflows.Scene"/> or <see cref="ImageWorkflows.Self"/>.</param>
/// <param name="Reference">The padded profile picture, for the "self" workflow.</param>
public record RenderRequest(string Workflow, string Prompt, int Width, int Height, long Seed, byte[]? Reference = null);

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

    /// <summary>The graph for <paramref name="request"/>, with the uploaded reference's name when it has one.</summary>
    public static JsonObject Build(RenderRequest request, ImageGenOptions options, string? referenceName = null)
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
            Set(graph, "Reference", "image", referenceName ?? throw new ArgumentException("The self workflow needs a reference image.", nameof(referenceName)));
        }
        else
        {
            Set(graph, "Model", "unet_name", options.SceneModel);
            Set(graph, "Text encoder", "clip_name", options.SceneTextEncoder);
            Set(graph, "VAE", "vae_name", options.SceneVae);
        }
        return graph;
    }

    /// <summary>The prompt for the "self" workflow: the scene, and that the face is the reference's.</summary>
    public static string SelfPrompt(string scene) =>
        $"{scene.Trim().TrimEnd('.')}. The person from the reference image is in this picture: keep their face and identity exactly the same.";

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
