namespace NeuralDamage.Infrastructure.Services.Attachments;

/// <summary>
/// Where uploaded files live. One folder per chat, so a chat's files go with
/// it, one folder for bots' profile pictures and one for the stickers.
/// </summary>
public interface IAttachmentStorage
{
    Task SaveAsync(Guid chatId, Guid attachmentId, byte[] data, CancellationToken ct = default);

    /// <summary>The file, or null when it is gone.</summary>
    Stream? OpenRead(Guid chatId, Guid attachmentId);

    Task<byte[]?> ReadAllAsync(Guid chatId, Guid attachmentId, CancellationToken ct = default);

    void Delete(Guid chatId, Guid attachmentId);

    void DeleteChat(Guid chatId);

    /// <summary>Stores a bot's profile picture, replacing the one it had.</summary>
    Task SaveBotAvatarAsync(Guid botId, byte[] data, CancellationToken ct = default);

    Task<byte[]?> ReadBotAvatarAsync(Guid botId, CancellationToken ct = default);

    void DeleteBotAvatar(Guid botId);

    /// <summary>Stores a sticker of the shared library.</summary>
    Task SaveStickerAsync(Guid stickerId, byte[] data, CancellationToken ct = default);

    Task<byte[]?> ReadStickerAsync(Guid stickerId, CancellationToken ct = default);

    /// <summary>Stores a story's PNG or a picture uploaded for one, by its id.</summary>
    Task SaveStoryFileAsync(Guid id, byte[] data, CancellationToken ct = default);

    Task<byte[]?> ReadStoryFileAsync(Guid id, CancellationToken ct = default);

    void DeleteStoryFile(Guid id);
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

    public void Delete(Guid chatId, Guid attachmentId)
    {
        var path = PathOf(chatId, attachmentId);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void DeleteChat(Guid chatId)
    {
        var folder = Path.Combine(root, chatId.ToString("N"));
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    public async Task SaveBotAvatarAsync(Guid botId, byte[] data, CancellationToken ct = default)
    {
        var path = AvatarPathOf(botId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data, ct);
    }

    public async Task<byte[]?> ReadBotAvatarAsync(Guid botId, CancellationToken ct = default)
    {
        var path = AvatarPathOf(botId);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public void DeleteBotAvatar(Guid botId)
    {
        var path = AvatarPathOf(botId);
        if (File.Exists(path))
            File.Delete(path);
    }

    public async Task SaveStickerAsync(Guid stickerId, byte[] data, CancellationToken ct = default)
    {
        var path = StickerPathOf(stickerId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data, ct);
    }

    public async Task<byte[]?> ReadStickerAsync(Guid stickerId, CancellationToken ct = default)
    {
        var path = StickerPathOf(stickerId);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public async Task SaveStoryFileAsync(Guid id, byte[] data, CancellationToken ct = default)
    {
        var path = StoryPathOf(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data, ct);
    }

    public async Task<byte[]?> ReadStoryFileAsync(Guid id, CancellationToken ct = default)
    {
        var path = StoryPathOf(id);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public void DeleteStoryFile(Guid id)
    {
        var path = StoryPathOf(id);
        if (File.Exists(path))
            File.Delete(path);
    }

    // Like "bots", "stories" can never be a chat's folder.
    private string StoryPathOf(Guid id) =>
        Path.Combine(root, "stories", id.ToString("N"));

    // Both parts are GUIDs, so nothing from the request can steer the path.
    private string PathOf(Guid chatId, Guid attachmentId) =>
        Path.Combine(root, chatId.ToString("N"), attachmentId.ToString("N"));

    // "bots" can never be a chat's folder, which is always 32 hex digits.
    private string AvatarPathOf(Guid botId) =>
        Path.Combine(root, "bots", botId.ToString("N"));

    // Like "bots", "stickers" can never be a chat's folder.
    private string StickerPathOf(Guid stickerId) =>
        Path.Combine(root, "stickers", stickerId.ToString("N"));
}
