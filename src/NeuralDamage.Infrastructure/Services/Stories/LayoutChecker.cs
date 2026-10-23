using System.Globalization;
using SkiaSharp;

namespace NeuralDamage.Infrastructure.Services.Stories;

/// <summary>A layer where it is really drawn, in pixels of the canvas.</summary>
/// <param name="Id">t1, t2… for text, s1… for stickers, i1… for images, in drawing order.</param>
public record LayerBox(int Index, string Id, string Type, SKRect Rect, StoryLayer Layer);

/// <summary>One thing wrong with a layout, with what to do about it.</summary>
/// <param name="Kind">overlap, offCanvas, smallText, lowContrast or tooManyLayers.</param>
/// <param name="Layers">The layer ids it is about.</param>
public record LayoutIssue(string Kind, IReadOnlyList<string> Layers, string Message);

/// <summary>What the layout check found; empty when the layout is fine.</summary>
public record LayoutReport(IReadOnlyList<LayoutIssue> Issues)
{
    public static readonly LayoutReport Clean = new([]);

    public bool IsClean => Issues.Count == 0;

    /// <summary>"overlap, lowContrast", for logs.</summary>
    public string Kinds => string.Join(", ", Issues.Select(i => i.Kind).Distinct());
}

/// <summary>
/// Checks a rendered layout for what makes it hard to read: text over other
/// text, stickers or pictures, layers cut off at the edge, text too small for
/// a phone, text without enough contrast against the pixels really behind it
/// (WCAG ratio), and too many layers. Every issue names the layers, where they
/// are in percent of the canvas and a concrete way out.
/// </summary>
public static class LayoutChecker
{
    /// <summary>Text smaller than this (in points of the 360-wide canvas) is hard to read on a phone.</summary>
    public const double MinTextSize = 16;

    /// <summary>WCAG's minimum for large text.</summary>
    public const double MinContrast = 3.0;

    /// <summary>More layers than this make a busy picture.</summary>
    public const int MaxCalmLayers = 8;

    /// <summary>Overlaps smaller than this share of the smaller box are left alone.</summary>
    private const float OverlapShare = 0.08f;

    /// <summary>How much of a box may stick out before it counts as cut off.</summary>
    private const float ClipShare = 0.04f;

    /// <summary>t1, s1, i1…: each layer's id by its type and order.</summary>
    public static List<string> Ids(StorySpec spec)
    {
        var counts = new Dictionary<char, int>();
        return spec.Layers.Select(l =>
        {
            var letter = l.Type switch { "text" => 't', "sticker" => 's', "image" => 'i', _ => 'x' };
            counts[letter] = counts.GetValueOrDefault(letter) + 1;
            return $"{letter}{counts[letter]}";
        }).ToList();
    }

    public static LayoutReport Check(StorySpec spec, IReadOnlyList<LayerBox> boxes, SKBitmap backdrop)
    {
        var (width, height) = (backdrop.Width, backdrop.Height);
        var issues = new List<LayoutIssue>();

        if (spec.Layers.Count > MaxCalmLayers)
            issues.Add(new("tooManyLayers", [], $"{spec.Layers.Count} layers make a busy picture; keep it to {MaxCalmLayers} or fewer."));

        foreach (var box in boxes)
        {
            if (Clipped(box, width, height) is { } clipped)
                issues.Add(clipped);
            if (box.Type == "text" && box.Layer.Size < MinTextSize)
                issues.Add(new("smallText", [box.Id],
                    $"text {box.Id} is size {Num(box.Layer.Size)}, hard to read on a phone; use at least {Num(MinTextSize + 4)}."));
        }

        // Text over anything drawn before or after it.
        foreach (var text in boxes.Where(b => b.Type == "text"))
            foreach (var other in boxes.Where(b => b.Index != text.Index && (b.Type != "text" || b.Index > text.Index)))
                if (Overlap(text, other, width, height) is { } overlap)
                    issues.Add(overlap);

        foreach (var text in boxes.Where(b => b.Type == "text"))
            if (Contrast(text, backdrop) is { } contrast)
                issues.Add(contrast);

        return new LayoutReport(issues);
    }

    private static LayoutIssue? Clipped(LayerBox box, int width, int height)
    {
        var r = box.Rect;
        var visible = SKRect.Intersect(r, SKRect.Create(0, 0, width, height));
        var area = r.Width * r.Height;
        if (area <= 0)
            return null;
        var shown = visible.IsEmpty ? 0 : visible.Width * visible.Height;
        if (shown >= area * (1 - ClipShare))
            return null;

        var sides = new List<string>();
        if (r.Left < 0) sides.Add("left");
        if (r.Right > width) sides.Add("right");
        if (r.Top < 0) sides.Add("top");
        if (r.Bottom > height) sides.Add("bottom");
        var what = $"{Name(box.Type)} {box.Id}";
        var where = $"x {Pct(r.Left, width)}–{Pct(r.Right, width)}%, y {Pct(r.Top, height)}–{Pct(r.Bottom, height)}%";
        if (shown <= 0)
            return new("offCanvas", [box.Id], $"{what} is entirely outside the canvas ({where}); move it inside, between 10% and 90%.");

        var fix = new List<string>();
        var halfW = r.Width / 2 / width * 100;
        var halfH = r.Height / 2 / height * 100;
        if (sides.Contains("left")) fix.Add($"x ≥ {Math.Ceiling(halfW + 2)}%");
        if (sides.Contains("right")) fix.Add($"x ≤ {Math.Floor(98 - halfW)}%");
        if (sides.Contains("top")) fix.Add($"y ≥ {Math.Ceiling(halfH + 2)}%");
        if (sides.Contains("bottom")) fix.Add($"y ≤ {Math.Floor(98 - halfH)}%");
        var move = fix.Count > 0 && halfW < 48 && halfH < 48 ? $"move it to {string.Join(" and ", fix)}, or shrink it" : "shrink it";
        return new("offCanvas", [box.Id], $"{what} runs off the {string.Join(" and ", sides)} edge ({where}); {move}.");
    }

    private static LayoutIssue? Overlap(LayerBox text, LayerBox other, int width, int height)
    {
        var shared = SKRect.Intersect(text.Rect, other.Rect);
        if (shared.IsEmpty)
            return null;
        var smaller = Math.Min(text.Rect.Width * text.Rect.Height, other.Rect.Width * other.Rect.Height);
        if (smaller <= 0 || shared.Width * shared.Height < smaller * OverlapShare)
            return null;

        // Move the text clear of the other layer, below it when there is room, above it otherwise.
        var halfH = text.Rect.Height / 2 / height * 100;
        var below = other.Rect.Bottom / height * 100 + halfH + 2;
        var above = other.Rect.Top / height * 100 - halfH - 2;
        var move = below <= 96 ? $"move {text.Id} to y ≥ {Math.Ceiling(below)}%"
            : above >= 4 ? $"move {text.Id} to y ≤ {Math.Floor(above)}%"
            : $"move {text.Id} to the side";
        var shrink = Math.Max(MinTextSize, Math.Round(text.Layer.Size * 0.75));
        return new("overlap", [text.Id, other.Id],
            $"text {text.Id} overlaps {Name(other.Type)} {other.Id} at y {Pct(shared.Top, height)}–{Pct(shared.Bottom, height)}%; {move}, or shrink it to size {Num(shrink)}.");
    }

    /// <summary>
    /// The contrast of a text layer against what is drawn under it, sampled
    /// across its box: the lower quartile, so a busy patch behind part of the
    /// text counts. When it is too low, where on the canvas it would read.
    /// </summary>
    private static LayoutIssue? Contrast(LayerBox text, SKBitmap backdrop)
    {
        var color = StoryRenderer.ParseColor(text.Layer.Color, SKColors.White);
        var ratio = SampledContrast(backdrop, text.Rect, color);
        if (ratio is not { } r || r >= MinContrast)
            return null;

        var behind = MedianLuminance(backdrop, text.Rect);
        var colour = behind > 0.35 ? "a dark text colour such as #111111" : "a light text colour such as #ffffff";
        var options = new List<string> { colour };
        if (text.Layer.Shadow != true)
            options.Add("a shadow (\"shadow\": true)");
        if (BestPlace(backdrop, text.Rect, color) is { } place)
            options.Add($"move it to y ≈ {place.Y}%, where it reads at {place.Ratio.ToString("0.0", CultureInfo.InvariantCulture)}:1");
        var on = text.Layer.Shadow == true ? "even with its shadow" : "on what is behind it";
        return new("lowContrast", [text.Id],
            $"text {text.Id} has contrast {r.ToString("0.0", CultureInfo.InvariantCulture)}:1 {on}; use {string.Join(", ", options.Take(options.Count - 1))}{(options.Count > 1 ? ", or " : "")}{options[^1]}.");
    }

    /// <summary>The lower quartile of the contrast between <paramref name="color"/> and the pixels in <paramref name="rect"/>; null when the box is off the canvas.</summary>
    public static double? SampledContrast(SKBitmap pixels, SKRect rect, SKColor color)
    {
        var samples = Samples(pixels, rect).Select(p => Ratio(Luminance(color), Luminance(p))).OrderBy(r => r).ToList();
        return samples.Count == 0 ? null : samples[samples.Count / 4];
    }

    private static double MedianLuminance(SKBitmap pixels, SKRect rect)
    {
        var values = Samples(pixels, rect).Select(Luminance).OrderBy(l => l).ToList();
        return values.Count == 0 ? 0 : values[values.Count / 2];
    }

    /// <summary>The band of the canvas, as tall as the text, where it would read best; null when nowhere reaches the minimum.</summary>
    private static (int Y, double Ratio)? BestPlace(SKBitmap pixels, SKRect rect, SKColor color)
    {
        (int, double)? best = null;
        var h = rect.Height;
        for (var centre = 8; centre <= 92; centre += 4)
        {
            var y = centre / 100f * pixels.Height;
            var band = new SKRect(rect.Left, y - h / 2, rect.Right, y + h / 2);
            if (band.Top < 0 || band.Bottom > pixels.Height)
                continue;
            if (SampledContrast(pixels, band, color) is { } r && r >= 4.5 && (best is null || r > best.Value.Item2))
                best = (centre, r);
        }
        return best;
    }

    private static IEnumerable<SKColor> Samples(SKBitmap pixels, SKRect rect)
    {
        var area = SKRect.Intersect(rect, SKRect.Create(0, 0, pixels.Width, pixels.Height));
        if (area.IsEmpty)
            yield break;
        const int columns = 24, rows = 8;
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var x = (int)(area.Left + (column + 0.5f) / columns * area.Width);
                var y = (int)(area.Top + (row + 0.5f) / rows * area.Height);
                yield return pixels.GetPixel(Math.Clamp(x, 0, pixels.Width - 1), Math.Clamp(y, 0, pixels.Height - 1));
            }
    }

    /// <summary>WCAG relative luminance.</summary>
    public static double Luminance(SKColor c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.Red) + 0.7152 * Channel(c.Green) + 0.0722 * Channel(c.Blue);
    }

    /// <summary>WCAG contrast ratio, 1 to 21.</summary>
    public static double Ratio(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

    private static string Name(string type) => type switch { "text" => "text", "sticker" => "sticker", _ => "image" };

    private static string Pct(float value, int of) => Math.Round(value / of * 100).ToString(CultureInfo.InvariantCulture);

    private static string Num(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The report as the bot reads it: one line per issue.</summary>
    public static string Describe(LayoutReport report) => string.Join("\n", report.Issues.Select(i => "- " + i.Message));
}
