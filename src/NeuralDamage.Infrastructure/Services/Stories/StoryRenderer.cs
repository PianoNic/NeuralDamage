using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace NeuralDamage.Infrastructure.Services.Stories;

/// <summary>The pictures a spec draws, already loaded: by sticker name and by image reference.</summary>
public record StoryAssets(byte[]? Background, IReadOnlyDictionary<string, byte[]> Stickers, IReadOnlyDictionary<string, byte[]> Images)
{
    public static readonly StoryAssets None = new(null, new Dictionary<string, byte[]>(), new Dictionary<string, byte[]>());
}

/// <summary>
/// Draws a spec into a PNG with Skia, the same for people and bots: 1080×1920
/// for a story, 1080×1350 or 1080×1080 for a post's picture
/// (<see cref="StoryFormats"/>). Text is Inter, with Noto Color Emoji for
/// emoji, both bundled in <c>Fonts/</c> so the image renders the same on a
/// bare Linux container as on a desktop. Animated stickers show their first
/// frame. A picture that is missing or does not decode is left out rather than
/// failing the render.
/// </summary>
public sealed class StoryRenderer : IDisposable
{
    /// <summary>A story's size; every format is this wide.</summary>
    public const int Width = 1080;
    public const int Height = 1920;

    /// <summary>Spec points to pixels: the editor's stage is 360 wide.</summary>
    public const float Scale = Width / 360f;

    /// <summary>Text wraps at this share of the story's width.</summary>
    private const float TextWidthShare = 0.85f;
    private const float LineHeight = 1.25f;

    private readonly SKTypeface _regular;
    private readonly SKTypeface _semiBold;
    private readonly SKTypeface _bold;
    private readonly SKTypeface _emoji;
    private readonly SKFont _regularProbe;
    private readonly SKFont _emojiProbe;
    private readonly Dictionary<SKTypeface, SKShaper> _shapers = [];
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);
    private readonly Lock _gate = new();

    public StoryRenderer(string? fontsDirectory = null)
    {
        var dir = fontsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Fonts");
        _regular = Load(dir, "Inter-Regular.ttf");
        _semiBold = Load(dir, "Inter-SemiBold.ttf");
        _bold = Load(dir, "Inter-Bold.ttf");
        _emoji = Load(dir, "NotoColorEmoji.ttf");
        _regularProbe = new SKFont(_regular);
        _emojiProbe = new SKFont(_emoji);
    }

    private static SKTypeface Load(string dir, string file) =>
        SKTypeface.FromFile(Path.Combine(dir, file)) ?? throw new FileNotFoundException($"The story font {file} is missing from {dir}.");

    /// <summary>The PNG of <paramref name="spec"/>, which must already be valid.</summary>
    public byte[] Render(StorySpec spec, StoryAssets assets)
    {
        // Skia objects are not shared across threads; one story at a time is plenty.
        lock (_gate)
        {
            var (width, height) = StoryFormats.Canvas(StoryFormats.Of(spec));
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(ParseColor(spec.Background.Color, SKColors.Black));

            if (assets.Background is { } bg && Decode(bg) is { } background)
                using (background)
                    DrawCover(canvas, background, width, height);

            foreach (var layer in spec.Layers)
            {
                var x = (float)(layer.X / 100 * width);
                var y = (float)(layer.Y / 100 * height);
                switch (layer.Type)
                {
                    case "text":
                        DrawText(canvas, layer, x, y, width);
                        break;
                    case "sticker" when layer.Name is not null && assets.Stickers.TryGetValue(layer.Name.Trim(), out var sticker):
                        DrawPicture(canvas, sticker, x, y, (float)layer.Size * Scale, (float)(layer.Rotation ?? 0), square: true);
                        break;
                    case "image" when layer.Image is not null && assets.Images.TryGetValue(layer.Image.Trim(), out var image):
                        DrawPicture(canvas, image, x, y, (float)layer.Size * Scale, (float)(layer.Rotation ?? 0), square: false);
                        break;
                }
            }

            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }

    /// <summary>Fills the canvas, centred, cutting what sticks out.</summary>
    private static void DrawCover(SKCanvas canvas, SKBitmap image, int width, int height)
    {
        var scale = Math.Max((float)width / image.Width, (float)height / image.Height);
        var w = image.Width * scale;
        var h = image.Height * scale;
        using var img = SKImage.FromBitmap(image);
        canvas.DrawImage(img, SKRect.Create((width - w) / 2, (height - h) / 2, w, h), Sampling, Paint());
    }

    /// <summary>
    /// A sticker fits a square of <paramref name="size"/>; an image is that wide
    /// and as tall as its shape needs. Both are centred on (x, y) and turned.
    /// </summary>
    private static void DrawPicture(SKCanvas canvas, byte[] bytes, float x, float y, float size, float rotation, bool square)
    {
        using var bitmap = Decode(bytes);
        if (bitmap is null)
            return;
        var scale = square ? Math.Min(size / bitmap.Width, size / bitmap.Height) : size / bitmap.Width;
        var w = bitmap.Width * scale;
        var h = bitmap.Height * scale;
        canvas.Save();
        canvas.Translate(x, y);
        if (rotation != 0)
            canvas.RotateDegrees(rotation);
        using (var img = SKImage.FromBitmap(bitmap))
            canvas.DrawImage(img, SKRect.Create(-w / 2, -h / 2, w, h), Sampling, Paint());
        canvas.Restore();
    }

    private static SKPaint Paint() => new() { IsAntialias = true };

    /// <summary>The first frame, for GIF and animated WebP as for any still image; null when it does not decode.</summary>
    private static SKBitmap? Decode(byte[] bytes)
    {
        try
        {
            return SKBitmap.Decode(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Centred lines at (x, y), wrapped at 85% of the width, emoji in colour.</summary>
    private void DrawText(SKCanvas canvas, StoryLayer layer, float x, float y, int canvasWidth)
    {
        var size = (float)layer.Size * Scale;
        var typeface = (layer.Weight ?? 400) switch
        {
            >= 650 => _bold,
            >= 550 => _semiBold,
            _ => _regular,
        };
        using var font = new SKFont(typeface, size) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var emojiFont = new SKFont(_emoji, size) { Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint { IsAntialias = true, Color = ParseColor(layer.Color, SKColors.White) };

        var lines = Wrap(layer.Text ?? "", font, emojiFont, canvasWidth * TextWidthShare);
        var lineHeight = size * LineHeight;
        var metrics = font.Metrics;
        // Centre each line's box (ascent to descent) on its slot.
        var baselineShift = -(metrics.Ascent + metrics.Descent) / 2;
        for (var i = 0; i < lines.Count; i++)
        {
            var lineY = y + (i - (lines.Count - 1) / 2f) * lineHeight + baselineShift;
            var runs = Runs(lines[i]);
            var width = runs.Sum(r => Measure(r, font, emojiFont));
            var at = x - width / 2;
            foreach (var run in runs)
            {
                var runFont = run.Emoji ? emojiFont : font;
                canvas.DrawShapedText(Shaper(runFont.Typeface), run.Text, at, lineY, SKTextAlign.Left, runFont, paint);
                at += Measure(run, font, emojiFont);
            }
        }
    }

    private SKShaper Shaper(SKTypeface typeface)
    {
        if (!_shapers.TryGetValue(typeface, out var shaper))
            _shapers[typeface] = shaper = new SKShaper(typeface);
        return shaper;
    }

    private float Measure(TextRun run, SKFont font, SKFont emojiFont)
    {
        var runFont = run.Emoji ? emojiFont : font;
        return Shaper(runFont.Typeface).Shape(run.Text, runFont).Width;
    }

    /// <summary>Words onto lines no wider than <paramref name="max"/>; explicit line breaks are kept. A word too long for a line gets one to itself.</summary>
    private List<string> Wrap(string text, SKFont font, SKFont emojiFont, float max)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";
                if (line.Length > 0 && Runs(candidate).Sum(r => Measure(r, font, emojiFont)) > max)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    private sealed record TextRun(string Text, bool Emoji);

    /// <summary>The text cut into stretches for Inter and stretches for the emoji font, by grapheme.</summary>
    private List<TextRun> Runs(string text)
    {
        var runs = new List<TextRun>();
        var current = new StringBuilder();
        var currentEmoji = false;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var grapheme = (string)elements.Current;
            var emoji = IsEmoji(grapheme);
            if (current.Length > 0 && emoji != currentEmoji)
            {
                runs.Add(new TextRun(current.ToString(), currentEmoji));
                current.Clear();
            }
            currentEmoji = emoji;
            current.Append(grapheme);
        }
        if (current.Length > 0)
            runs.Add(new TextRun(current.ToString(), currentEmoji));
        return runs;
    }

    /// <summary>A grapheme Inter cannot draw but the emoji font can, or one asking for emoji presentation.</summary>
    private bool IsEmoji(string grapheme)
    {
        var first = char.ConvertToUtf32(grapheme, 0);
        if (first < 0x80)
            return false;
        if (grapheme.Contains('️') || grapheme.Contains('‍') || first >= 0x1F000)
            return _emojiProbe.ContainsGlyph(first);
        return !_regularProbe.ContainsGlyph(first) && _emojiProbe.ContainsGlyph(first);
    }

    /// <summary>"#rgb" or "#rrggbb"; <paramref name="fallback"/> otherwise.</summary>
    public static SKColor ParseColor(string? value, SKColor fallback)
    {
        if (!StorySpecRules.IsColor(value))
            return fallback;
        var hex = value![1..];
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        return new SKColor(
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public void Dispose()
    {
        foreach (var shaper in _shapers.Values)
            shaper.Dispose();
        _regularProbe.Dispose();
        _emojiProbe.Dispose();
        _regular.Dispose();
        _semiBold.Dispose();
        _bold.Dispose();
        _emoji.Dispose();
    }
}
