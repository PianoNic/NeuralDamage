namespace NeuralDamage.Infrastructure.Services.Attachments;

/// <summary>Where uploaded files live. One folder per chat, so a chat's files go with it.</summary>
public interface IAttachmentStorage
{
    Task SaveAsync(Guid chatId, Guid attachmentId, byte[] data, CancellationToken ct = default);

    /// <summary>The file, or null when it is gone.</summary>
    Stream? OpenRead(Guid chatId, Guid attachmentId);

    Task<byte[]?> ReadAllAsync(Guid chatId, Guid attachmentId, CancellationToken ct = default);

    void DeleteChat(Guid chatId);
}

public class FileSystemAttachmentStorage(string root) : IAttachmentStorage
{
    public async Task SaveAsync(Guid chatId, Guid attachmentId, byte[] data, CancellationToken ct = default)
    {
        var path = PathOf(chatId, attachmentId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data, ct);
    }

    public Stream? OpenRead(Guid chatId, Guid attachmentId)
    {
        var path = PathOf(chatId, attachmentId);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public async Task<byte[]?> ReadAllAsync(Guid chatId, Guid attachmentId, CancellationToken ct = default)
    {
        var path = PathOf(chatId, attachmentId);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public void DeleteChat(Guid chatId)
    {
        var folder = Path.Combine(root, chatId.ToString("N"));
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    // Both parts are GUIDs, so nothing from the request can steer the path.
    private string PathOf(Guid chatId, Guid attachmentId) =>
        Path.Combine(root, chatId.ToString("N"), attachmentId.ToString("N"));
}
