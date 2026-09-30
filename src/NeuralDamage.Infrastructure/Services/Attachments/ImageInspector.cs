using System.Buffers.Binary;

namespace NeuralDamage.Infrastructure.Services.Attachments;

public record ImageInfo(string ContentType, int? Width, int? Height);

/// <summary>
/// Tells PNG, JPEG, WebP and GIF apart by their first bytes, never by the name
/// or the type the browser claimed, and reads the size from the header when it
/// can. Anything else is not an image this app accepts.
/// </summary>
public static class ImageInspector
{
    public static readonly IReadOnlyList<string> AllowedTypes = ["image/png", "image/jpeg", "image/webp", "image/gif"];

    public static ImageInfo? Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return new("image/png", (int)BinaryPrimitives.ReadUInt32BigEndian(data[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(data[20..]));

        if (data.Length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
            return new("image/gif", BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));

        if (data.Length >= 16 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            var (width, height) = WebPSize(data);
            return new("image/webp", width, height);
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            var (width, height) = JpegSize(data);
            return new("image/jpeg", width, height);
        }

        return null;
    }

    private static (int?, int?) WebPSize(ReadOnlySpan<byte> data)
    {
        var chunk = data[12..16];
        if (chunk.SequenceEqual("VP8 "u8) && data.Length >= 30)
            return (BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF);
        if (chunk.SequenceEqual("VP8L"u8) && data.Length >= 25)
        {
            int b0 = data[21], b1 = data[22], b2 = data[23], b3 = data[24];
            return (1 + (((b1 & 0x3F) << 8) | b0), 1 + (((b3 & 0x0F) << 10) | (b2 << 2) | ((b1 & 0xC0) >> 6)));
        }
        if (chunk.SequenceEqual("VP8X"u8) && data.Length >= 30)
            return (1 + (data[24] | data[25] << 8 | data[26] << 16), 1 + (data[27] | data[28] << 8 | data[29] << 16));
        return (null, null);
    }

    /// <summary>Walks the segments to the first start-of-frame, which holds the size.</summary>
    private static (int?, int?) JpegSize(ReadOnlySpan<byte> data)
    {
        var i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF) return (null, null);
            var marker = data[i + 1];
            if (marker == 0xFF) { i++; continue; }
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9)) { i += 2; continue; }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return (BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]));

            i += 2 + BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
        }
        return (null, null);
    }
}
