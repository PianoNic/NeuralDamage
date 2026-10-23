namespace NeuralDamage.Infrastructure.Services.Images;

/// <summary>
/// The self-hosted ComfyUI that draws pictures, bound from the <c>ImageGen</c>
/// section. Without a <see cref="BaseUrl"/> the feature is off and nothing
/// offers to generate a picture.
/// </summary>
public class ImageGenOptions
{
    public const string SectionName = "ImageGen";

    /// <summary>Where ComfyUI listens, e.g. <c>http://comfyui.example.com:8188</c>. Only ever from configuration.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Sent as a bearer token when set, for a proxy in front of ComfyUI; ComfyUI itself needs none.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The most pictures made in a day (in <c>App:TimeZone</c>), by people and bots together.</summary>
    public int MaxPerDay { get; set; } = 100;

    /// <summary>How long one picture may take once it is its turn. The first after a quiet spell also loads the model.</summary>
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often ComfyUI is asked whether the picture is done.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    // The "scene" workflow: Z-Image Turbo.
    public string SceneModel { get; set; } = "z_image_turbo_int8_convrot.safetensors";
    public string SceneTextEncoder { get; set; } = "qwen_3_4b_fp8_mixed.safetensors";
    public string SceneVae { get; set; } = "ae.safetensors";

    // The "self" workflow: Qwen-Image-Edit 2511 with the bot's picture as the reference.
    public string SelfModel { get; set; } = "qwen-image-edit-2511-Q3_K_M.gguf";
    public string SelfTextEncoder { get; set; } = "qwen_2.5_vl_7b_fp8_scaled.safetensors";
    public string SelfVae { get; set; } = "qwen_image_vae.safetensors";
    public string SelfLora { get; set; } = "Qwen-Image-Edit-2511-Lightning-4steps-V1.0-bf16.safetensors";

    public bool Enabled => BaseUri is not null;

    /// <summary>The base URL with a trailing slash, so paths resolve under it; null when unset or not http(s).</summary>
    public Uri? BaseUri =>
        Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/")
            : null;
}
