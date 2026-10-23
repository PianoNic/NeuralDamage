using NeuralDamage.Infrastructure.Services.Stories;
using SkiaSharp;

namespace NeuralDamage.Tests.Stories;

/// <summary>The PNG a story spec turns into, with the bundled fonts and the native Skia library.</summary>
public class StoryRendererTests
{
    private static readonly StoryRenderer Renderer = new();

    /// <summary>A plain square of one colour, made here: no real sticker or picture is used.</summary>
    public static byte[] Square(SKColor color, int width = 64, int height = 64, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static SKBitmap Render(StorySpec spec, StoryAssets? assets = null)
    {
        var png = Renderer.Render(spec, assets ?? StoryAssets.None);
        if (Environment.GetEnvironmentVariable("STORY_RENDER_OUT") is { Length: > 0 } dir)
            File.WriteAllBytes(Path.Combine(dir, $"story-{Guid.NewGuid():N}.png"), png);
        return SKBitmap.Decode(png);
    }

    private static bool Near(SKColor a, SKColor b, int tolerance = 12) =>
        Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance && Math.Abs(a.Blue - b.Blue) <= tolerance;

    [Test]
    public async Task ASpec_RendersA1080By1920Png_WithItsBackground_Text_AndSticker()
    {
        var spec = new StorySpec(new StoryBackground("#1f3a2b"),
        [
            new StoryLayer("text", 50, 25, 40, Text: "Kitchen closed 🔥", Color: "#ffffff", Weight: 700),
            new StoryLayer("sticker", 50, 70, 100, Name: "red-square", Rotation: 0),
        ]);
        using var bitmap = Render(spec, new StoryAssets(null, new Dictionary<string, byte[]> { ["red-square"] = Square(SKColors.Red) }, new Dictionary<string, byte[]>()));

        await Assert.That(bitmap.Width).IsEqualTo(1080);
        await Assert.That(bitmap.Height).IsEqualTo(1920);
        await Assert.That(Near(bitmap.GetPixel(5, 5), new SKColor(0x1f, 0x3a, 0x2b))).IsTrue();
        // The sticker: 100 points is 300 pixels, centred at 70% down.
        await Assert.That(Near(bitmap.GetPixel(540, (int)(1920 * 0.7)), SKColors.Red)).IsTrue();
        await Assert.That(Near(bitmap.GetPixel(540 + 170, (int)(1920 * 0.7)), new SKColor(0x1f, 0x3a, 0x2b))).IsTrue();

        // The text band holds white pixels, and the emoji colour (not just white and green).
        var band = Enumerable.Range(0, 1080).SelectMany(x => Enumerable.Range(400, 160).Select(y => bitmap.GetPixel(x, y))).ToList();
        await Assert.That(band.Count(p => p.Red > 230 && p.Green > 230 && p.Blue > 230)).IsGreaterThan(500);
        await Assert.That(band.Count(p => p.Red > 200 && p.Green < 160 && p.Blue < 80)).IsGreaterThan(50);
    }

    [Test]
    public async Task ABackgroundImage_CoversTheWholeStory()
    {
        var spec = new StorySpec(new StoryBackground("#000000", "upload:" + Guid.NewGuid(), "cover"), []);
        using var bitmap = Render(spec, new StoryAssets(Square(SKColors.Blue, 300, 120, SKEncodedImageFormat.Jpeg), new Dictionary<string, byte[]>(), new Dictionary<string, byte[]>()));

        foreach (var (x, y) in new[] { (2, 2), (1077, 2), (2, 1917), (1077, 1917), (540, 960) })
            await Assert.That(Near(bitmap.GetPixel(x, y), SKColors.Blue, 20)).IsTrue();
    }

    [Test]
    public async Task APictureThatDoesNotDecode_IsLeftOut()
    {
        var spec = new StorySpec(new StoryBackground("#fafafa"), [new StoryLayer("image", 50, 50, 200, Image: "wiki:Nothing")]);
        using var bitmap = Render(spec, new StoryAssets(null, new Dictionary<string, byte[]>(), new Dictionary<string, byte[]> { ["wiki:Nothing"] = [1, 2, 3] }));
        await Assert.That(Near(bitmap.GetPixel(540, 960), new SKColor(0xfa, 0xfa, 0xfa))).IsTrue();
    }
}
