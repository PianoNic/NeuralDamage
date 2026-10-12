using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Infrastructure.Services.Stickers;

/// <summary>What a sticker may be, and how its name is written.</summary>
public static partial class StickerRules
{
    public const int MaxNameLength = 32;
    public const int MaxDescriptionLength = 200;
    public const long MaxBytes = 4 * 1024 * 1024;

    /// <summary>The browser scales a still sticker down to this before upload.</summary>
    public const int MaxStaticSide = 512;

    /// <summary>Animated ones keep their frames, and so their size, up to this.</summary>
    public const int MaxAnimatedSide = 1024;

    public static readonly IReadOnlyList<string> AllowedTypes = ["image/png", "image/webp", "image/gif"];

    /// <summary>"Cat Laughing!.png" → "cat-laughing-png"; null when nothing usable is left.</summary>
    public static string? Slugify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var ascii = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                ascii.Append(c);
        var slug = NotNameChars().Replace(ascii.ToString().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > MaxNameLength)
            slug = slug[..MaxNameLength].TrimEnd('-');
        return slug.Length == 0 ? null : slug;
    }

    public static bool IsValidName(string name) => name.Length <= MaxNameLength && NamePattern().IsMatch(name);

    /// <summary>The sniffed image, refused unless it is a PNG, WebP or GIF; with whether it moves.</summary>
    public static (ImageInfo Image, bool Animated)? Inspect(ReadOnlySpan<byte> data)
    {
        if (ImageInspector.Inspect(data) is not { } image || !AllowedTypes.Contains(image.ContentType))
            return null;
        var animated = image.ContentType switch
        {
            "image/gif" => GifFrames(data) > 1,
            "image/webp" => data.Length >= 21 && data[12..16].SequenceEqual("VP8X"u8) && (data[20] & 0x02) != 0,
            "image/png" => HasPngChunkBeforeData(data, "acTL"u8),
            _ => false,
        };
        return (image, animated);
    }

    /// <summary>Counts image descriptors, walking the blocks; stops at two, which is all it needs.</summary>
    private static int GifFrames(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13) return 0;
        var i = 13;
        if ((data[10] & 0x80) != 0)
            i += 3 * (1 << ((data[10] & 0x07) + 1));
        var frames = 0;
        while (i < data.Length && frames < 2)
        {
            switch (data[i])
            {
                case 0x2C:
                    frames++;
                    if (i + 10 > data.Length) return frames;
                    var packed = data[i + 9];
                    i += 10;
                    if ((packed & 0x80) != 0)
                        i += 3 * (1 << ((packed & 0x07) + 1));
                    i++; // LZW minimum code size
                    i = SkipSubBlocks(data, i);
                    break;
                case 0x21:
                    i = SkipSubBlocks(data, i + 2);
                    break;
                default:
                    return frames; // 0x3B trailer, or something broken
            }
        }
        return frames;
    }

    private static int SkipSubBlocks(ReadOnlySpan<byte> data, int i)
    {
        while (i < data.Length && data[i] != 0)
            i += data[i] + 1;
        return i + 1;
    }

    private static bool HasPngChunkBeforeData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> type)
    {
        var i = 8;
        while (i + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[i..]);
            var chunk = data[(i + 4)..(i + 8)];
            if (chunk.SequenceEqual(type)) return true;
            if (chunk.SequenceEqual("IDAT"u8) || length > int.MaxValue - 12) return false;
            i += (int)length + 12;
        }
        return false;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotNameChars();

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex NamePattern();
}
