using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NeuralDamage.Infrastructure.Services.Music;

/// <summary>A whole song downloaded to <see cref="File"/>, a temporary file the caller moves or deletes.</summary>
public record FullSong(string File, string ContentType, int Seconds);

public interface IFullSongFetcher
{
    /// <summary>Whether whole songs can be fetched at all.</summary>
    bool IsAvailable { get; }

    /// <summary>The whole song, found by its artist and title and its length; null when none was found.</summary>
    Task<FullSong?> FetchAsync(string artist, string title, int durationSeconds, CancellationToken ct = default);
}

/// <summary>
/// Fetches whole songs with yt-dlp: the first of a few search results whose length is
/// close to Deezer's, audio only, as it is (no ffmpeg needed).
/// </summary>
public class YtDlpSongFetcher(IOptions<MusicOptions> options, SongAudioStore store, ILogger<YtDlpSongFetcher> logger) : IFullSongFetcher
{
    private readonly MusicOptions _options = options.Value;
    private readonly Lazy<string?> _executable = new(() => Resolve(options.Value.YtDlpPath));

    public bool IsAvailable => _executable.Value is not null;

    public async Task<FullSong?> FetchAsync(string artist, string title, int durationSeconds, CancellationToken ct = default)
    {
        if (_executable.Value is not { } exe)
            return null;

        var work = Path.Combine(store.Root, ".incoming", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in Arguments(artist, title, durationSeconds, work, Path.Combine(store.Root, ".yt-dlp-cache"), _options.MaxAudioMegabytes))
            start.ArgumentList.Add(argument);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.YtDlpTimeout);
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            var found = Parse(await output, work);
            if (found is null)
                logger.LogInformation("yt-dlp found no whole song for {Artist} - {Title} (exit {Code}): {Errors}",
                    artist, title, process.ExitCode, Cut(await errors));
            else
                found = found with { File = MoveOut(found.File) };
            return found;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("yt-dlp took longer than {Timeout} for {Artist} - {Title}", _options.YtDlpTimeout, artist, title);
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            logger.LogWarning(e, "yt-dlp could not run for {Artist} - {Title}", artist, title);
            return null;
        }
        finally
        {
            if (!process.HasExited())
                process.Kill(entireProcessTree: true);
            TryDelete(work);
        }
    }

    /// <summary>
    /// What yt-dlp is run with: a search for the song, taking the first result whose length is within
    /// a few seconds of Deezer's, so a live version or an hour-long loop isn't picked.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string artist, string title, int durationSeconds, string folder, string cacheFolder, int maxMegabytes)
    {
        var slack = Math.Max(10, durationSeconds / 12);
        var arguments = new List<string>
        {
            "--quiet", "--no-warnings", "--no-progress", "--no-playlist", "--no-mtime",
            "--max-downloads", "1",
            "--max-filesize", $"{maxMegabytes}M",
            "--format", "bestaudio[ext=m4a]/bestaudio[ext=webm]/bestaudio",
            "--cache-dir", cacheFolder,
            "--output", Path.Combine(folder, "song.%(ext)s"),
            "--print", "after_move:%(duration)s|%(ext)s|%(filepath)s",
        };
        if (durationSeconds > 0)
            arguments.AddRange(["--match-filter", $"duration>={Math.Max(1, durationSeconds - slack)} & duration<={durationSeconds + slack}"]);
        arguments.Add($"ytsearch5:{artist} - {title} audio");
        return arguments;
    }

    /// <summary>The song yt-dlp printed it saved, when the file is there.</summary>
    public static FullSong? Parse(string output, string folder)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', 3);
            if (parts.Length != 3 || ContentTypeOf(parts[1]) is not { } type)
                continue;
            var file = Path.GetFullPath(parts[2]);
            if (!file.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                continue;
            var seconds = double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0;
            return new FullSong(file, type, seconds);
        }
        return null;
    }

    public static string? ContentTypeOf(string extension) => extension.Trim().ToLowerInvariant() switch
    {
        "m4a" or "mp4" => "audio/mp4",
        "webm" => "audio/webm",
        "opus" or "ogg" => "audio/ogg",
        "mp3" => "audio/mpeg",
        _ => null,
    };

    /// <summary>Out of the work folder, which goes away when the fetch is done.</summary>
    private string MoveOut(string file)
    {
        var kept = Path.Combine(store.Root, ".incoming", Guid.NewGuid().ToString("N"));
        File.Move(file, kept);
        return kept;
    }

    private static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        path = path.Trim();
        if (Path.IsPathRooted(path) || path.Contains('/') || path.Contains('\\'))
            return File.Exists(path) ? path : null;

        var names = OperatingSystem.IsWindows() ? new[] { path + ".exe", path } : [path];
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(dir => names.Select(name => Path.Combine(dir, name)))
            .FirstOrDefault(File.Exists);
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Cut(string text) => text.Length > 400 ? text[..400] : text.Trim();
}

internal static class ProcessExtensions
{
    public static bool HasExited(this Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            // Never started.
            return true;
        }
    }
}
