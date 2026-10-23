using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Stories;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using SkiaSharp;

namespace NeuralDamage.Tests.Stories;

/// <summary>The layout report: each check on crafted specs, from the renderer's own measurements and pixels.</summary>
public class LayoutCheckTests
{
    private static readonly StoryRenderer Renderer = FeedHarness.SharedRenderer;

    private static byte[] Solid(SKColor color, int width = 200, int height = 200)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A photo-like picture: a bright, noisy sky over a dark, noisy ground, split at <paramref name="horizon"/>.</summary>
    private static byte[] Photo(int width = 768, int height = 1344, double horizon = 0.6)
    {
        var random = new Random(7);
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sky = y < height * horizon;
                var n = random.Next(-25, 25);
                bitmap.SetPixel(x, y, sky
                    ? new SKColor((byte)Math.Clamp(200 + n, 0, 255), (byte)Math.Clamp(225 + n, 0, 255), 250)
                    : new SKColor((byte)Math.Clamp(40 + n, 0, 255), (byte)Math.Clamp(50 + n, 0, 255), (byte)Math.Clamp(30 + n, 0, 255)));
            }
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private static StoryAssets Assets(byte[]? background = null, Dictionary<string, byte[]>? stickers = null) =>
        new(background, stickers ?? [], new Dictionary<string, byte[]>());

    private static StorySpec Spec(params StoryLayer[] layers) => new(new StoryBackground("#1d3347"), [.. layers]);

    private static StoryLayer Text(string text, double x, double y, double size = 28, string color = "#ffffff", bool? shadow = null) =>
        new("text", x, y, size, Text: text, Color: color, Weight: 700, Shadow: shadow);

    [Test]
    public async Task ACleanLayout_HasNoIssues()
    {
        var report = Renderer.CheckLayout(Spec(Text("Hello", 50, 20), new StoryLayer("sticker", 50, 70, 130, Name: "cat")),
            Assets(stickers: new() { ["cat"] = Solid(SKColors.Orange) }));

        await Assert.That(report.Issues).IsEmpty();
    }

    [Test]
    public async Task TextOverASticker_IsReported_WithWhereAndHowToMoveIt()
    {
        var report = Renderer.CheckLayout(Spec(Text("Productivity is up", 50, 60), new StoryLayer("sticker", 50, 62, 150, Name: "cat")),
            Assets(stickers: new() { ["cat"] = Solid(SKColors.Orange) }));

        var issue = report.Issues.Single(i => i.Kind == "overlap");
        await Assert.That(issue.Layers).IsEquivalentTo(["t1", "s1"]);
        await Assert.That(issue.Message).StartsWith("text t1 overlaps sticker s1 at y ");
        await Assert.That(issue.Message).Contains("move t1 to y ≥ ");
        await Assert.That(issue.Message).Contains("or shrink it to size 21");
    }

    [Test]
    public async Task TheTextBox_ComesFromRealMeasurement_SoAWrappedTextReachesFurther()
    {
        // The same centre: one word clears the text below it, a long text that wraps onto several lines does not.
        var below = Text("below", 50, 62, 24);
        var shortText = Renderer.CheckLayout(Spec(Text("Hi", 50, 50, 40), below), Assets());
        var longText = Renderer.CheckLayout(Spec(Text("This sentence is long enough to wrap onto several lines on the story", 50, 50, 40), below), Assets());

        await Assert.That(shortText.Issues.Any(i => i.Kind == "overlap")).IsFalse();
        await Assert.That(longText.Issues.Single(i => i.Kind == "overlap").Layers).IsEquivalentTo(["t1", "t2"]);
    }

    [Test]
    public async Task ALayerOffTheEdge_IsReported_WithTheSide()
    {
        var report = Renderer.CheckLayout(Spec(new StoryLayer("sticker", 97, 50, 200, Name: "cat")), Assets(stickers: new() { ["cat"] = Solid(SKColors.Orange) }));

        var issue = report.Issues.Single(i => i.Kind == "offCanvas");
        await Assert.That(issue.Message).Contains("sticker s1 runs off the right edge");
        await Assert.That(issue.Message).Contains("move it to x ≤ ");
    }

    [Test]
    public async Task TinyText_IsReported()
    {
        var report = Renderer.CheckLayout(Spec(Text("fine print", 50, 50, 12)), Assets());

        await Assert.That(report.Issues.Single(i => i.Kind == "smallText").Message).Contains("text t1 is size 12");
    }

    [Test]
    public async Task TooManyLayers_AreReported()
    {
        var layers = Enumerable.Range(0, 10).Select(i => Text($"line {i}", 50, 5 + i * 10, 18)).ToArray();

        var report = Renderer.CheckLayout(Spec(layers), Assets());

        await Assert.That(report.Issues.Any(i => i.Kind == "tooManyLayers")).IsTrue();
    }

    [Test]
    public async Task LowContrast_IsMeasuredOnTheRenderedPhoto_AndPointsToWhereItReads()
    {
        var photo = Assets(Photo());
        var spec = new StorySpec(new StoryBackground("#000000", "upload:x", "cover"), [Text("Beach day", 50, 20, 36)]);

        var white = Renderer.CheckLayout(spec, photo);
        var dark = Renderer.CheckLayout(spec with { Layers = [Text("Beach day", 50, 20, 36, "#111111")] }, photo);
        var low = Renderer.CheckLayout(spec with { Layers = [Text("Beach day", 50, 85, 36)] }, photo);

        var issue = white.Issues.Single(i => i.Kind == "lowContrast");
        await Assert.That(issue.Message).StartsWith("text t1 has contrast 1.");
        await Assert.That(issue.Message).Contains("dark text colour");
        await Assert.That(issue.Message).Contains("\"shadow\": true");
        await Assert.That(issue.Message).Contains("move it to y ≈ ");
        await Assert.That(dark.Issues).IsEmpty();
        await Assert.That(low.Issues).IsEmpty();
    }

    [Test]
    public async Task AShadow_IsPartOfThePixelsBehindTheText()
    {
        var plain = new StorySpec(new StoryBackground("#e8e8e8"), [Text("SALE", 50, 50, 60)]);
        var shadowed = plain with { Layers = [Text("SALE", 50, 50, 60, shadow: true)] };

        var before = Renderer.CheckLayout(plain, Assets());
        var after = Renderer.CheckLayout(shadowed, Assets());

        await Assert.That(before.Issues.Any(i => i.Kind == "lowContrast")).IsTrue();
        // The shadow darkens what is behind the letters, so the measured contrast goes up.
        using var withShadow = SKBitmap.Decode(Renderer.Render(shadowed, Assets()));
        using var without = SKBitmap.Decode(Renderer.Render(plain, Assets()));
        var darker = 0;
        for (var y = 860; y < 1060; y += 4)
            for (var x = 300; x < 780; x += 4)
                if (withShadow.GetPixel(x, y).Red < without.GetPixel(x, y).Red - 20)
                    darker++;
        await Assert.That(darker).IsGreaterThan(50);
        await Assert.That(after.Issues.Count(i => i.Kind == "lowContrast")).IsLessThanOrEqualTo(before.Issues.Count(i => i.Kind == "lowContrast"));
    }

    [Test]
    public async Task Ids_GoByTypeAndOrder()
    {
        var ids = LayoutChecker.Ids(Spec(Text("a", 50, 10), new StoryLayer("sticker", Name: "x"), Text("b", 50, 20), new StoryLayer("image", Image: "wiki:x")));

        await Assert.That(ids).IsEquivalentTo(["t1", "s1", "t2", "i1"]);
    }
}

/// <summary>The bot's one look at its own layout.</summary>
public class LayoutRevisionTests
{
    private const string Overlapping = """
        {"background":{"color":"#1d3347"},"layers":[
          {"type":"text","text":"Kitchen closed","x":50,"y":50,"size":40,"color":"#ffffff"},
          {"type":"text","text":"go home","x":50,"y":52,"size":40,"color":"#ffffff"}]}
        """;

    private const string Fixed = """
        {"background":{"color":"#1d3347"},"layers":[
          {"type":"text","text":"Kitchen closed","x":50,"y":30,"size":40,"color":"#ffffff"},
          {"type":"text","text":"go home","x":50,"y":70,"size":40,"color":"#ffffff"}]}
        """;

    private static async Task<(FeedHarness H, StoryWriter Writer, IServiceScope Scope)> CreateAsync(params string[] replies)
    {
        var h = await FeedHarness.CreateAsync(botCount: 1);
        h.Reply(replies);
        var scope = h.Provider.CreateScope();
        return (h, scope.ServiceProvider.GetRequiredService<StoryWriter>(), scope);
    }

    private static int Calls(FeedHarness h) =>
        h.OpenRouter.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IOpenRouterService.GenerateResponseAsync));

    [Test]
    public async Task AReport_GoesBackOnce_AndTheRevisedSpecIsPosted()
    {
        var (h, writer, scope) = await CreateAsync(Overlapping, Fixed);
        using var _h = h;
        using var _s = scope;
        List<ChatMessage>? second = null;
        var replies = new Queue<string>([Overlapping, Fixed]);
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            if (replies.Count == 1)
                second = ci.ArgAt<List<ChatMessage>>(3);
            return replies.Dequeue();
        });

        var story = await writer.WriteAsync(h.Bots[0], DateTimeOffset.UtcNow);

        await Assert.That(story).IsNotNull();
        await Assert.That(second![^1].Content).Contains("text t1 overlaps text t2");
        await Assert.That(second[^1].Content).Contains("\"keep\": true");
        var saved = StorySpecRules.Parse((await h.Db.Stories.AsNoTracking().SingleAsync()).Spec).Spec!;
        await Assert.That(saved.Layers.Select(l => l.Y)).IsEquivalentTo([30.0, 70.0]);
    }

    [Test]
    public async Task ABotThatMeantIt_KeepsItsLayout()
    {
        var (h, writer, scope) = await CreateAsync(Overlapping, """{"keep": true, "reason": "the overlap is the joke"}""");
        using var _h = h;
        using var _s = scope;

        await writer.WriteAsync(h.Bots[0], DateTimeOffset.UtcNow);

        var saved = StorySpecRules.Parse((await h.Db.Stories.AsNoTracking().SingleAsync()).Spec).Spec!;
        await Assert.That(saved.Layers.Select(l => l.Y)).IsEquivalentTo([50.0, 52.0]);
        await Assert.That(Calls(h)).IsEqualTo(2);
    }

    [Test]
    public async Task ThereIsOnlyOneRound_AndPostingIsNeverBlocked()
    {
        // The revision is as bad as the first try: it is posted anyway, without a third call.
        var (h, writer, scope) = await CreateAsync(Overlapping, Overlapping, Fixed);
        using var _h = h;
        using var _s = scope;

        var story = await writer.WriteAsync(h.Bots[0], DateTimeOffset.UtcNow);

        await Assert.That(story).IsNotNull();
        await Assert.That(Calls(h)).IsEqualTo(2);
    }

    [Test]
    public async Task AnUnusableRevision_LeavesTheFirstSpec()
    {
        var (h, writer, scope) = await CreateAsync(Overlapping, "sorry, I can't do JSON today");
        using var _h = h;
        using var _s = scope;

        var story = await writer.WriteAsync(h.Bots[0], DateTimeOffset.UtcNow);

        await Assert.That(story).IsNotNull();
        var saved = StorySpecRules.Parse((await h.Db.Stories.AsNoTracking().SingleAsync()).Spec).Spec!;
        await Assert.That(saved.Layers.Select(l => l.Y)).IsEquivalentTo([50.0, 52.0]);
    }

    [Test]
    public async Task ACleanLayout_CostsNoSecondCall()
    {
        var (h, writer, scope) = await CreateAsync(Fixed);
        using var _h = h;
        using var _s = scope;

        await writer.WriteAsync(h.Bots[0], DateTimeOffset.UtcNow);

        await Assert.That(Calls(h)).IsEqualTo(1);
    }

    [Test]
    public async Task ABotsComposedPostPicture_GetsTheSameRound()
    {
        var feed = new FeedOptions { ChatInspiredShare = 0, ImageShare = 1 };
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: feed);
        var post = "{\"text\": \"closing\", \"image\": {\"spec\": " + Overlapping.Replace("{\"background\"", "{\"format\":\"post-1x1\",\"background\"") + "}}";
        h.Reply(post, Fixed);

        var made = await h.Scheduler.PostAsync(h.Bots[0], DateTimeOffset.UtcNow, 0.5, imageRoll: 0);

        var image = await h.Db.GeneratedImages.AsNoTracking().SingleAsync(i => i.Id == made!.ImageId);
        var spec = StorySpecRules.Parse(image.Spec).Spec!;
        await Assert.That(spec.Format).IsEqualTo("post-1x1");
        await Assert.That(spec.Layers.Select(l => l.Y)).IsEquivalentTo([30.0, 70.0]);
    }

    [Test]
    public async Task Answers_AreRead()
    {
        await Assert.That(StoryWriter.ParseRevision("""{"keep": true, "reason": "on purpose"}""").Keep).IsEqualTo("on purpose");
        await Assert.That(StoryWriter.ParseRevision("```json\n" + Fixed + "\n```").Revised!.Layers.Count).IsEqualTo(2);
        await Assert.That(StoryWriter.ParseRevision("""{"spec": """ + Fixed + "}").Revised!.Layers.Count).IsEqualTo(2);
        await Assert.That(StoryWriter.ParseRevision("no idea")).IsEqualTo(((StorySpec?)null, (string?)null));
    }
}
