using SkiaSharp;

namespace NeuralDamage.Infrastructure.Services.Images;

/// <summary>
/// Turns a bot's profile picture into the reference the "self" workflow
/// needs. Profile pictures are tight crops of the face, and the edit model
/// keeps a face far better with some room around it, so the picture gets a
/// margin in the colour of its own edge and is scaled to a fixed size.
/// </summary>
public static class ReferenceImage
{
    /// <summary>The margin on each side, as a share of the picture's longer side (192 px on a 512 px crop).</summary>
    public const double Margin = 0.375;

    /// <summary>The longer side of the result.</summary>
    public const int Size = 768;

    /// <returns>A PNG.</returns>
    /// <exception cref="ImageGenerationException">The picture cannot be read.</exception>
    public static byte[] Prepare(byte[] picture)
    {
        using var bitmap = Decode(picture) ?? throw new ImageGenerationException("The profile picture cannot be read.");
        var pad = (int)Math.Round(Math.Max(bitmap.Width, bitmap.Height) * Margin);
        var paddedWidth = bitmap.Width + 2 * pad;
        var paddedHeight = bitmap.Height + 2 * pad;
        var scale = (float)Size / Math.Max(paddedWidth, paddedHeight);
        var width = Math.Max(1, (int)Math.Round(paddedWidth * scale));
        var height = Math.Max(1, (int)Math.Round(paddedHeight * scale));

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new ImageGenerationException("The profile picture cannot be prepared.");
        var canvas = surface.Canvas;
        canvas.Clear(EdgeColor(bitmap));
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, SKRect.Create(pad * scale, pad * scale, bitmap.Width * scale, bitmap.Height * scale),
            new SKSamplingOptions(SKCubicResampler.Mitchell));

        using var snapshot = surface.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static SKBitmap? Decode(byte[] picture)
    {
        try
        {
            return SKBitmap.Decode(picture);
        }
        catch (ArgumentException)
        {
            // Skia finds no codec for it.
            return null;
        }
    }

    /// <summary>The average colour of the outermost pixels, opaque.</summary>
    public static SKColor EdgeColor(SKBitmap bitmap)
    {
        long r = 0, g = 0, b = 0, n = 0;
        void Add(int x, int y)
        {
            var c = bitmap.GetPixel(x, y);
            // A transparent edge counts as white, which is how it would show.
            var a = c.Alpha / 255.0;
            r += (long)(c.Red * a + 255 * (1 - a));
            g += (long)(c.Green * a + 255 * (1 - a));
            b += (long)(c.Blue * a + 255 * (1 - a));
            n++;
        }
        for (var x = 0; x < bitmap.Width; x++)
        {
            Add(x, 0);
            Add(x, bitmap.Height - 1);
        }
        for (var y = 1; y < bitmap.Height - 1; y++)
        {
            Add(0, y);
            Add(bitmap.Width - 1, y);
        }
        return n == 0 ? SKColors.White : new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}
