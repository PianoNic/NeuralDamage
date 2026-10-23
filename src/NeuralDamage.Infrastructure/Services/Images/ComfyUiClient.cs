using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NeuralDamage.Infrastructure.Services.Images;

/// <summary>Draws one picture; <see cref="ComfyUiClient"/> in the app.</summary>
public interface IImageRenderer
{
    /// <returns>The picture's bytes, a PNG.</returns>
    /// <exception cref="ImageGenerationException">The generator refused or failed.</exception>
    Task<byte[]> RenderAsync(RenderRequest request, CancellationToken ct = default);
}

/// <summary>The generator said no, or broke; the message is short enough to show.</summary>
public class ImageGenerationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Talks to ComfyUI's own HTTP API: uploads the reference picture
/// (<c>/upload/image</c>), queues the workflow (<c>/prompt</c>), polls
/// <c>/history/{id}</c> until it is done and fetches the result from
/// <c>/view</c>. When the caller gives up, the job is taken off ComfyUI's
/// queue and interrupted, so the GPU is free for the next one.
/// </summary>
public class ComfyUiClient(HttpClient http, IOptions<ImageGenOptions> options, ILogger<ComfyUiClient> logger) : IImageRenderer
{
    private readonly ImageGenOptions _options = options.Value;

    /// <summary>Points the client at <c>ImageGen:BaseUrl</c>, with the key as a bearer token when there is one.</summary>
    public static void Configure(HttpClient http, ImageGenOptions options)
    {
        if (options.BaseUri is { } baseUri)
            http.BaseAddress = baseUri;
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());
        // One request never takes long; the whole job has its own, longer limit.
        http.Timeout = TimeSpan.FromMinutes(2);
    }

    public async Task<byte[]> RenderAsync(RenderRequest request, CancellationToken ct = default)
    {
        if (http.BaseAddress is null)
            throw new ImageGenerationException("Image generation is not set up.");

        var reference = request.Reference is { } bytes ? await UploadAsync(bytes, ct) : null;
        var graph = ImageWorkflows.Build(request, _options, reference);
        var promptId = await QueueAsync(graph, ct);
        try
        {
            var output = await WaitAsync(promptId, ct);
            return await FetchAsync(output, ct);
        }
        catch (OperationCanceledException)
        {
            await CancelAsync(promptId);
            throw;
        }
    }

    /// <summary>Uploads a reference picture into ComfyUI's input folder; returns the name a LoadImage node takes.</summary>
    public async Task<string> UploadAsync(byte[] png, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        // Quoted, as browsers send them; without quotes .NET also adds a filename* that some parsers trip on.
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"image\"", FileName = $"\"neuraldamage-{Guid.NewGuid():N}.png\"" };
        form.Add(file);
        var overwrite = new StringContent("true");
        overwrite.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"overwrite\"" };
        form.Add(overwrite);

        using var response = await SendAsync(() => http.PostAsync("upload/image", form, ct), "upload the reference picture");
        var body = await ReadJsonAsync(response, ct);
        var name = (string?)body["name"] ?? throw new ImageGenerationException("The image generator did not take the reference picture.");
        var subfolder = (string?)body["subfolder"];
        return string.IsNullOrEmpty(subfolder) ? name : $"{subfolder}/{name}";
    }

    /// <summary>Queues the graph; returns its prompt id.</summary>
    public async Task<string> QueueAsync(JsonObject graph, CancellationToken ct = default)
    {
        var payload = new JsonObject { ["prompt"] = graph, ["client_id"] = Guid.NewGuid().ToString("N") };
        HttpResponseMessage sent;
        try
        {
            sent = await http.PostAsync("prompt", Json(payload), ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ImageGenerationException("The image generator cannot be reached.", ex);
        }
        using var response = sent;
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("ComfyUI refused the workflow ({Status}): {Body}", (int)response.StatusCode, Cut(text, 800));
            throw new ImageGenerationException((int)response.StatusCode is >= 400 and < 500
                ? "The image generator refused the request."
                : $"The image generator could not queue the picture ({(int)response.StatusCode}).");
        }
        var body = await ReadJsonAsync(response, ct);
        return (string?)body["prompt_id"] ?? throw new ImageGenerationException("The image generator did not queue the request.");
    }

    /// <summary>Polls the history until the prompt is done; returns its first image output.</summary>
    private async Task<JsonObject> WaitAsync(string promptId, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using (var response = await SendAsync(() => http.GetAsync($"history/{Uri.EscapeDataString(promptId)}", ct), "check on the picture"))
            {
                var history = await ReadJsonAsync(response, ct);
                if (history[promptId] is JsonObject entry)
                {
                    var status = entry["status"] as JsonObject;
                    if ((string?)status?["status_str"] == "error")
                    {
                        logger.LogWarning("ComfyUI failed prompt {PromptId}: {Messages}", promptId, Cut(status?["messages"]?.ToJsonString() ?? "", 800));
                        throw new ImageGenerationException("The image generator failed to draw the picture.");
                    }
                    var image = (entry["outputs"] as JsonObject)?
                        .Select(o => o.Value?["images"] as JsonArray)
                        .SelectMany(images => images ?? [])
                        .OfType<JsonObject>()
                        .FirstOrDefault();
                    if (image is not null)
                        return image;
                    if (status?["completed"]?.GetValue<bool>() == true)
                        throw new ImageGenerationException("The image generator finished without a picture.");
                }
            }
            await Task.Delay(_options.PollInterval, ct);
        }
    }

    private async Task<byte[]> FetchAsync(JsonObject image, CancellationToken ct)
    {
        var query = string.Join('&', new[] { "filename", "subfolder", "type" }
            .Select(key => $"{key}={Uri.EscapeDataString((string?)image[key] ?? "")}"));
        using var response = await SendAsync(() => http.GetAsync($"view?{query}", ct), "fetch the picture");
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Takes the prompt off ComfyUI's queue and stops it if it is running; best effort.</summary>
    private async Task CancelAsync(string promptId)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var _ = await http.PostAsync("queue", Json(new JsonObject { ["delete"] = new JsonArray(promptId) }), cts.Token);
            using var __ = await http.PostAsync("interrupt", Json(new JsonObject { ["prompt_id"] = promptId }), cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            logger.LogInformation("Could not cancel ComfyUI prompt {PromptId}: {Error}", promptId, ex.Message);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send, string what)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (HttpRequestException ex)
        {
            throw new ImageGenerationException("The image generator cannot be reached.", ex);
        }
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new ImageGenerationException($"The image generator could not {what} ({status}).");
        }
        return response;
    }

    private static async Task<JsonObject> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject
                ?? throw new ImageGenerationException("The image generator answered with something unexpected.");
        }
        catch (JsonException ex)
        {
            throw new ImageGenerationException("The image generator answered with something unexpected.", ex);
        }
    }

    /// <summary>A JSON body with its length, not chunked: not every server or proxy in front of ComfyUI takes chunked requests.</summary>
    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

    private static string Cut(string text, int max) => text.Length > max ? text[..max] + "..." : text;
}
