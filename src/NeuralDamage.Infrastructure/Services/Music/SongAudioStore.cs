namespace NeuralDamage.Infrastructure.Services.Music;

/// <summary>
/// Where songs' audio lives: a <c>music</c> folder of the attachment storage, with each
/// song's preview and, once fetched, the whole song, named by its id. Served straight
/// from disk, so players can seek.
/// </summary>
public class SongAudioStore(string root)
{
    public string Root { get; } = root;

    /// <summary>The song's preview or whole song; the file may not exist.</summary>
    public string PathOf(Guid songId, bool full) => Path.Combine(Root, $"{songId:N}.{(full ? "full" : "preview")}");

    public async Task SavePreviewAsync(Guid songId, byte[] data, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Root);
        await File.WriteAllBytesAsync(PathOf(songId, full: false), data, ct);
    }

    /// <summary>Keeps <paramref name="file"/> as the whole song; it is moved.</summary>
    public void SaveFull(Guid songId, string file)
    {
        Directory.CreateDirectory(Root);
        File.Move(file, PathOf(songId, full: true), overwrite: true);
    }

    public void Delete(Guid songId)
    {
        foreach (var path in new[] { PathOf(songId, false), PathOf(songId, true) })
            if (File.Exists(path))
                File.Delete(path);
    }
}
