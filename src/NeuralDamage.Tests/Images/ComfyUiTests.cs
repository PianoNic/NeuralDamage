using System.Text.Json.Nodes;
using NeuralDamage.Infrastructure.Services.Images;
using NeuralDamage.Tests.Helpers;
using SkiaSharp;

namespace NeuralDamage.Tests.Images;

/// <summary>The ComfyUI client against a fake ComfyUI: queue, poll, fetch, and what goes wrong.</summary>
public class ComfyUiClientTests
{
    private static RenderRequest Scene(string prompt = "a cat holding a sign that says \"MONTAG\"") =>
        new(ImageWorkflows.Scene, prompt, 1024, 1024, 42);

    [Test]
    public async Task AScene_IsQueuedPolledAndFetched()
    {
        await using var comfy = await FakeComfyUi.StartAsync();
        comfy.PollsBeforeDone = 3;

        var png = await comfy.Client().RenderAsync(Scene());

        await Assert.That(png).IsEquivalentTo(comfy.Picture);
        await Assert.That(comfy.Requests.Count(r => r.StartsWith("GET /history/"))).IsEqualTo(3);
        await Assert.That(comfy.Requests.Last()).IsEqualTo("GET /view");
        await Assert.That(comfy.Uploads).IsEmpty();
        var graph = comfy.Graphs.Single();
        await Assert.That((string?)ImageWorkflows.Node(graph, "Prompt")["inputs"]!["text"]).IsEqualTo("a cat holding a sign that says \"MONTAG\"");
    }

    [Test]
    public async Task WithAKey_ItIsSentAsABearerToken_AndWithoutOne_NoAuthorizationAtAll()
    {
        await using var comfy = await FakeComfyUi.StartAsync();

        await comfy.Client(apiKey: "secret-key").RenderAsync(Scene());
        await Assert.That(comfy.Authorizations.Distinct()).IsEquivalentTo(new string?[] { "Bearer secret-key" });

        while (comfy.Authorizations.TryDequeue(out _)) { }
        await comfy.Client().RenderAsync(Scene());
        await Assert.That(comfy.Authorizations.Distinct()).IsEquivalentTo(new string?[] { null });
    }

    [Test]
    public async Task TheSelfWorkflow_UploadsTheReferenceFirst_AndLoadsItByName()
    {
        await using var comfy = await FakeComfyUi.StartAsync();

        await comfy.Client().RenderAsync(new RenderRequest(ImageWorkflows.Self, "this person cooking", 1024, 1024, 7, [TestImages.Png(768, 768)]));

        await Assert.That(comfy.Requests.First()).IsEqualTo("POST /upload/image");
        var uploaded = comfy.Uploads.Single();
        var graph = comfy.Graphs.Single();
        await Assert.That((string?)ImageWorkflows.Node(graph, "Reference")["inputs"]!["image"]).IsEqualTo(uploaded);
        await Assert.That((string?)ImageWorkflows.Node(graph, "Prompt")["inputs"]!["prompt"]).IsEqualTo("this person cooking");
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task TheSelfWorkflow_GetsEveryReference_AsImage1ToImage3(int count)
    {
        await using var comfy = await FakeComfyUi.StartAsync();
        var references = Enumerable.Range(0, count).Select(_ => TestImages.Png(768, 768)).ToList();

        await comfy.Client().RenderAsync(new RenderRequest(ImageWorkflows.Self, "this person at the beach", 1024, 1024, 7, references));

        var uploaded = comfy.Uploads.ToList();
        await Assert.That(uploaded.Count).IsEqualTo(count);
        var graph = comfy.Graphs.Single();
        var loaded = graph.Select(n => n.Value!.AsObject())
            .Where(n => (string?)n["class_type"] == "LoadImage")
            .Select(n => (string)n["inputs"]!["image"]!)
            .ToList();
        await Assert.That(loaded).IsEquivalentTo(uploaded);

        var encoders = graph.Select(n => n.Value!.AsObject()).Where(n => (string?)n["class_type"] == "TextEncodeQwenImageEditPlus").ToList();
        await Assert.That(encoders.Count).IsEqualTo(2);
        foreach (var encoder in encoders)
        {
            var inputs = encoder["inputs"]!.AsObject();
            for (var i = 1; i <= 3; i++)
                await Assert.That(inputs.ContainsKey($"image{i}")).IsEqualTo(i <= count);
            // Each slot loads its own picture, through its own scale node.
            var images = Enumerable.Range(1, count).Select(i =>
            {
                var scale = graph[(string)inputs[$"image{i}"]![0]!]!;
                var load = graph[(string)scale["inputs"]!["image"]![0]!]!;
                return ((string?)scale["class_type"], (string)load["inputs"]!["image"]!);
            }).ToList();
            await Assert.That(images.Select(x => x.Item1).Distinct()).IsEquivalentTo(new string?[] { "ImageScaleToTotalPixels" });
            await Assert.That(images.Select(x => x.Item2)).IsEquivalentTo(uploaded);
        }
        var ids = graph.Select(n => n.Key).ToHashSet();
        var links = graph.SelectMany(n => n.Value!["inputs"]!.AsObject()).Select(i => i.Value).OfType<JsonArray>().Select(a => (string)a[0]!);
        await Assert.That(links.All(ids.Contains)).IsTrue();
    }

    [Test]
    public async Task TheSelfWorkflow_TakesNoMoreThanThree_AndNotNone()
    {
        var four = Enumerable.Range(0, 4).Select(i => $"ref{i}.png").ToList();
        var request = new RenderRequest(ImageWorkflows.Self, "x", 1024, 1024, 1);

        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(ImageWorkflows.Build(request, new ImageGenOptions(), four)));
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(ImageWorkflows.Build(request, new ImageGenOptions(), [])));
    }

    [Test]
    public async Task ARefusedWorkflow_FailsWithAShortMessage()
    {
        await using var comfy = await FakeComfyUi.StartAsync();
        comfy.Mode = FakeComfyUi.Behaviour.RefusesPrompt;

        var error = await Assert.ThrowsAsync<ImageGenerationException>(() => comfy.Client().RenderAsync(Scene()));
        await Assert.That(error!.Message).IsEqualTo("The image generator refused the request.");
    }

    [Test]
    public async Task AFailedRun_Fails()
    {
        await using var comfy = await FakeComfyUi.StartAsync();
        comfy.Mode = FakeComfyUi.Behaviour.FailsRunning;

        var error = await Assert.ThrowsAsync<ImageGenerationException>(() => comfy.Client().RenderAsync(Scene()));
        await Assert.That(error!.Message).IsEqualTo("The image generator failed to draw the picture.");
    }

    [Test]
    public async Task AGeneratorThatIsDown_Fails()
    {
        await using var comfy = await FakeComfyUi.StartAsync();
        comfy.Mode = FakeComfyUi.Behaviour.Down;

        var error = await Assert.ThrowsAsync<ImageGenerationException>(() => comfy.Client().RenderAsync(Scene()));
        await Assert.That(error!.Message).Contains("502");
    }

    [Test]
    public async Task APictureThatNeverComes_IsCancelledAtTheTimeout_AndTakenOffComfyUisQueue()
    {
        await using var comfy = await FakeComfyUi.StartAsync();
        comfy.Mode = FakeComfyUi.Behaviour.NeverFinishes;
        using var queue = new ImageQueue(TimeSpan.FromMilliseconds(300));
        var client = comfy.Client();

        await Assert.ThrowsAsync<TimeoutException>(() => queue.EnqueueAsync(token => client.RenderAsync(Scene(), token)));

        await Assert.That(comfy.Requests).Contains("POST /queue");
        await Assert.That(comfy.Requests).Contains("POST /interrupt");
    }
}

/// <summary>How a request's parameters go into the shipped workflows.</summary>
public class ImageWorkflowTests
{
    [Test]
    public async Task TheScene_TakesThePromptSizeSeedAndTheConfiguredModels()
    {
        var options = new ImageGenOptions { SceneModel = "other_unet.safetensors" };
        var graph = ImageWorkflows.Build(new RenderRequest(ImageWorkflows.Scene, "a red apple", 768, 1344, 123), options);

        await Assert.That((string?)ImageWorkflows.Node(graph, "Prompt")["inputs"]!["text"]).IsEqualTo("a red apple");
        await Assert.That((int)ImageWorkflows.Node(graph, "Size")["inputs"]!["width"]!).IsEqualTo(768);
        await Assert.That((int)ImageWorkflows.Node(graph, "Size")["inputs"]!["height"]!).IsEqualTo(1344);
        await Assert.That((long)ImageWorkflows.Node(graph, "Sampler")["inputs"]!["seed"]!).IsEqualTo(123);
        await Assert.That((string?)ImageWorkflows.Node(graph, "Model")["inputs"]!["unet_name"]).IsEqualTo("other_unet.safetensors");
        await Assert.That((string?)ImageWorkflows.Node(graph, "Text encoder")["inputs"]!["type"]).IsEqualTo("lumina2");
        await Assert.That((string?)ImageWorkflows.Node(graph, "Sampler")["inputs"]!["sampler_name"]).IsEqualTo("res_multistep");
        await Assert.That((int)ImageWorkflows.Node(graph, "Sampler")["inputs"]!["steps"]!).IsEqualTo(8);
    }

    [Test]
    public async Task TheSelf_TakesTheReferenceAndTheEditModels_ByDefault()
    {
        var graph = ImageWorkflows.Build(new RenderRequest(ImageWorkflows.Self, "this person surfing", 1024, 1024, 5), new ImageGenOptions(), ["ref.png"]);

        await Assert.That((string?)ImageWorkflows.Node(graph, "Reference")["inputs"]!["image"]).IsEqualTo("ref.png");
        await Assert.That((string?)ImageWorkflows.Node(graph, "Model")["inputs"]!["unet_name"]).IsEqualTo("qwen-image-edit-2511-Q3_K_M.gguf");
        await Assert.That((string?)ImageWorkflows.Node(graph, "LoRA")["inputs"]!["lora_name"]).IsEqualTo("Qwen-Image-Edit-2511-Lightning-4steps-V1.0-bf16.safetensors");
        await Assert.That((string?)ImageWorkflows.Node(graph, "Prompt")["inputs"]!["prompt"]).IsEqualTo("this person surfing");
        // Every reference to another node still points at a node of the graph.
        var ids = graph.Select(n => n.Key).ToHashSet();
        var links = graph.SelectMany(n => n.Value!["inputs"]!.AsObject()).Select(i => i.Value).OfType<JsonArray>().Select(a => (string)a[0]!);
        await Assert.That(links.All(ids.Contains)).IsTrue();
    }

    [Test]
    public async Task EachBuild_IsAFreshCopy()
    {
        var a = ImageWorkflows.Build(new RenderRequest(ImageWorkflows.Scene, "one", 1024, 1024, 1), new ImageGenOptions());
        var b = ImageWorkflows.Build(new RenderRequest(ImageWorkflows.Scene, "two", 1024, 1024, 2), new ImageGenOptions());
        await Assert.That((string?)ImageWorkflows.Node(a, "Prompt")["inputs"]!["text"]).IsEqualTo("one");
        await Assert.That((string?)ImageWorkflows.Node(b, "Prompt")["inputs"]!["text"]).IsEqualTo("two");
    }

    [Test]
    public async Task TheBaseUrl_AloneTurnsTheFeatureOn()
    {
        await Assert.That(new ImageGenOptions().Enabled).IsFalse();
        await Assert.That(new ImageGenOptions { BaseUrl = "  " }.Enabled).IsFalse();
        await Assert.That(new ImageGenOptions { BaseUrl = "ftp://comfyui.example.com" }.Enabled).IsFalse();
        await Assert.That(new ImageGenOptions { BaseUrl = "http://comfyui.example.com:8188" }.BaseUri!.AbsoluteUri).IsEqualTo("http://comfyui.example.com:8188/");
        await Assert.That(new ImageGenOptions { BaseUrl = "https://example.com/comfy" }.BaseUri!.AbsoluteUri).IsEqualTo("https://example.com/comfy/");
    }

    [Test]
    public async Task AProfilePicture_GetsAMarginInItsEdgeColour_At768()
    {
        using var bitmap = new SKBitmap(512, 512);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(200, 30, 30));
            using var paint = new SKPaint { Color = SKColors.Blue };
            canvas.DrawCircle(256, 256, 120, paint);
        }
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);

        var prepared = ReferenceImage.Prepare(data.ToArray());

        using var result = SKBitmap.Decode(prepared);
        await Assert.That(result.Width).IsEqualTo(768);
        await Assert.That(result.Height).IsEqualTo(768);
        var corner = result.GetPixel(5, 5);
        await Assert.That(corner.Red).IsEqualTo((byte)200);
        await Assert.That(corner.Blue).IsEqualTo((byte)30);
        var middle = result.GetPixel(384, 384);
        await Assert.That(middle.Blue).IsGreaterThan((byte)200);
    }

    [Test]
    public async Task SomethingThatIsNoPicture_CannotBeAReference()
    {
        await Assert.ThrowsAsync<ImageGenerationException>(() => Task.FromResult(ReferenceImage.Prepare([1, 2, 3])));
    }
}

/// <summary>One picture at a time, each with its own time limit.</summary>
public class ImageQueueTests
{
    [Test]
    public async Task OnlyOnePictureIsDrawnAtATime_InTheOrderAsked()
    {
        using var queue = new ImageQueue(TimeSpan.FromSeconds(10));
        var running = 0;
        var most = 0;
        var order = new List<int>();

        var jobs = Enumerable.Range(0, 5).Select(i => queue.EnqueueAsync(async token =>
        {
            var now = Interlocked.Increment(ref running);
            lock (order)
            {
                most = Math.Max(most, now);
                order.Add(i);
            }
            await Task.Delay(20, token);
            Interlocked.Decrement(ref running);
            return i;
        })).ToList();

        var results = await Task.WhenAll(jobs);

        await Assert.That(most).IsEqualTo(1);
        await Assert.That(order).IsEquivalentTo(new[] { 0, 1, 2, 3, 4 });
        await Assert.That(results).IsEquivalentTo(new[] { 0, 1, 2, 3, 4 });
        await Assert.That(queue.Waiting).IsEqualTo(0);
    }

    [Test]
    public async Task APictureOverItsTime_TimesOut_AndTheNextOneStillRuns()
    {
        using var queue = new ImageQueue(TimeSpan.FromMilliseconds(100));
        var slow = queue.EnqueueAsync(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        });
        var next = queue.EnqueueAsync(_ => Task.FromResult(1));

        await Assert.ThrowsAsync<TimeoutException>(() => slow);
        await Assert.That(await next).IsEqualTo(1);
    }

    [Test]
    public async Task AFailure_ReachesTheOneWhoAsked()
    {
        using var queue = new ImageQueue(TimeSpan.FromSeconds(10));
        var failing = queue.EnqueueAsync<int>(_ => throw new ImageGenerationException("nope"));
        var error = await Assert.ThrowsAsync<ImageGenerationException>(() => failing);
        await Assert.That(error!.Message).IsEqualTo("nope");
    }
}
