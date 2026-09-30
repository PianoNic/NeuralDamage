using System.Buffers.Binary;
using System.Collections.Concurrent;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Tests.Helpers;

/// <summary>Just enough of each format's header for sniffing and sizing.</summary>
public static class TestImages
{
    public static byte[] Png(int width = 64, int height = 32)
    {
        var data = new byte[64];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R'];
        signature.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), (uint)height);
        return data;
    }

    public static byte[] Gif(int width, int height)
    {
        var data = new byte[32];
        "GIF89a"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)height);
        return data;
    }

    /// <summary>SOI, an APP0 segment to skip, then a baseline start-of-frame.</summary>
    public static byte[] Jpeg(int width, int height)
    {
        byte[] data =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0, 0, 0, 0, 0x03, 0, 0, 0, 0, 0, 0, 0, 0,
        ];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(13), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(15), (ushort)width);
        return data;
    }

    public static byte[] WebP(int width, int height)
    {
        var data = new byte[40];
        "RIFF"u8.CopyTo(data);
        "WEBP"u8.CopyTo(data.AsSpan(8));
        "VP8X"u8.CopyTo(data.AsSpan(12));
        data[24] = (byte)(width - 1); data[25] = (byte)((width - 1) >> 8); data[26] = (byte)((width - 1) >> 16);
        data[27] = (byte)(height - 1); data[28] = (byte)((height - 1) >> 8); data[29] = (byte)((height - 1) >> 16);
        return data;
    }
}

public sealed class InMemoryAttachmentStorage : IAttachmentStorage
{
    public ConcurrentDictionary<(Guid Chat, Guid Id), byte[]> Files { get; } = new();

    public Task SaveAsync(Guid chatId, Guid attachmentId, byte[] data, CancellationToken ct = default)
    {
        Files[(chatId, attachmentId)] = data;
        return Task.CompletedTask;
    }

    public Stream? OpenRead(Guid chatId, Guid attachmentId) =>
        Files.TryGetValue((chatId, attachmentId), out var data) ? new MemoryStream(data) : null;

    public Task<byte[]?> ReadAllAsync(Guid chatId, Guid attachmentId, CancellationToken ct = default) =>
        Task.FromResult(Files.TryGetValue((chatId, attachmentId), out var data) ? data : null);

    public void Delete(Guid chatId, Guid attachmentId) => Files.TryRemove((chatId, attachmentId), out _);

    public void DeleteChat(Guid chatId)
    {
        foreach (var key in Files.Keys.Where(k => k.Chat == chatId))
            Files.TryRemove(key, out _);
    }
}
