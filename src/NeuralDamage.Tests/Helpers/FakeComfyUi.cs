using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuralDamage.Infrastructure.Services.Images;

namespace NeuralDamage.Tests.Helpers;

/// <summary>
/// Just enough of ComfyUI's HTTP API for the client: <c>/upload/image</c>,
/// <c>/prompt</c>, <c>/history/{id}</c>, <c>/view</c>, <c>/queue</c> and
/// <c>/interrupt</c>, in memory, with the failures a real one has.
/// </summary>
public sealed class FakeComfyUi : IAsyncDisposable
{
    public enum Behaviour
    {
        /// <summary>Draws the picture after <see cref="PollsBeforeDone"/> polls.</summary>
        Draws,
        /// <summary>Refuses the workflow, as a missing model does.</summary>
        RefusesPrompt,
        /// <summary>Takes the workflow, then fails while running it.</summary>
        FailsRunning,
        /// <summary>Takes the workflow and never finishes.</summary>
        NeverFinishes,
        /// <summary>Answers everything with a 502, as a proxy does when ComfyUI is down.</summary>
        Down,
    }

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, int> _polls = new();

    public Behaviour Mode { get; set; } = Behaviour.Draws;
    public int PollsBeforeDone { get; set; } = 2;
    public byte[] Picture { get; set; } = TestImages.Png(1024, 1024);

    public ConcurrentQueue<string> Requests { get; } = new();
    public ConcurrentQueue<string?> Authorizations { get; } = new();
    public ConcurrentQueue<string> Uploads { get; } = new();
    public ConcurrentQueue<JsonObject> Graphs { get; } = new();

    private FakeComfyUi(WebApplication app) => _app = app;

    public static async Task<FakeComfyUi> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var fake = new FakeComfyUi(app);
        fake.Map(app);
        await app.StartAsync();
        return fake;
    }

    /// <summary>A client of the app's, wired to this server.</summary>
    public ComfyUiClient Client(string? apiKey = null, TimeSpan? poll = null)
    {
        var options = new ImageGenOptions { BaseUrl = "http://comfyui.example.com/", ApiKey = apiKey, PollInterval = poll ?? TimeSpan.FromMilliseconds(10) };
        var http = new HttpClient(_app.GetTestServer().CreateHandler());
        ComfyUiClient.Configure(http, options);
        return new ComfyUiClient(http, Options.Create(options), NullLogger<ComfyUiClient>.Instance);
    }

    private void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            Requests.Enqueue($"{context.Request.Method} {context.Request.Path}");
            Authorizations.Enqueue(context.Request.Headers.Authorization.FirstOrDefault());
            if (Mode == Behaviour.Down)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }
            await next(context);
        });

        app.MapPost("/upload/image", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var file = form.Files["image"];
            if (file is null || form["overwrite"] != "true")
                return Results.BadRequest();
            Uploads.Enqueue(file.FileName);
            return Results.Json(new { name = file.FileName, subfolder = "", type = "input" });
        });

        app.MapPost("/prompt", async (HttpRequest request) =>
        {
            // Some servers and proxies in front of ComfyUI refuse chunked bodies.
            if (request.ContentLength is null)
                return Results.StatusCode(StatusCodes.Status411LengthRequired);
            var body = (await JsonNode.ParseAsync(request.Body))!.AsObject();
            Graphs.Enqueue(body["prompt"]!.AsObject());
            if (Mode == Behaviour.RefusesPrompt)
                return Results.Json(new { error = new { type = "prompt_outputs_failed_validation", message = "Value not in list" }, node_errors = new { } }, statusCode: 400);
            var id = Guid.NewGuid().ToString();
            _polls[id] = 0;
            return Results.Json(new { prompt_id = id, number = 1, node_errors = new { } });
        });

        app.MapGet("/history/{id}", (string id) =>
        {
            var polls = _polls.AddOrUpdate(id, 1, (_, n) => n + 1);
            if (Mode == Behaviour.NeverFinishes || polls < PollsBeforeDone)
                return Results.Json(new { });
            if (Mode == Behaviour.FailsRunning)
                return Results.Json(new JsonObject
                {
                    [id] = new JsonObject
                    {
                        ["outputs"] = new JsonObject(),
                        ["status"] = new JsonObject { ["status_str"] = "error", ["completed"] = false, ["messages"] = new JsonArray("execution_error") },
                    },
                });
            return Results.Json(new JsonObject
            {
                [id] = new JsonObject
                {
                    ["outputs"] = new JsonObject
                    {
                        ["10"] = new JsonObject { ["images"] = new JsonArray(new JsonObject { ["filename"] = $"{id}.png", ["subfolder"] = "", ["type"] = "output" }) },
                    },
                    ["status"] = new JsonObject { ["status_str"] = "success", ["completed"] = true, ["messages"] = new JsonArray() },
                },
            });
        });

        app.MapGet("/view", (string filename, string type) =>
            type == "output" && _polls.ContainsKey(filename.Replace(".png", ""))
                ? Results.Bytes(Picture, "image/png")
                : Results.NotFound());

        app.MapPost("/queue", () => Results.Ok());
        app.MapPost("/interrupt", () => Results.Ok());
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
