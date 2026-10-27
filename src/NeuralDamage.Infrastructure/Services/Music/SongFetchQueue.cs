using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NeuralDamage.Infrastructure.Services.Music;

/// <summary>
/// Fetches whole songs one at a time, in the background, so picking a song never waits on
/// yt-dlp; and now and then sweeps the songs nothing uses any more.
/// </summary>
public class SongFetchQueue(IServiceScopeFactory scopes, ILogger<SongFetchQueue> logger) : BackgroundService
{
    public static readonly TimeSpan SweepEvery = TimeSpan.FromHours(6);

    private readonly Channel<Guid> _songs = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    /// <summary>Asks for the whole song; asking again while it is on its way does nothing.</summary>
    public void Request(Guid songId)
    {
        if (_pending.TryAdd(songId, 0))
            _songs.Writer.TryWrite(songId);
    }

    /// <summary>Whether the whole song is waiting or being fetched.</summary>
    public bool IsPending(Guid songId) => _pending.ContainsKey(songId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sweep = SweepLoopAsync(stoppingToken);
        try
        {
            await foreach (var songId in _songs.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<SongLibrary>().FetchFullAsync(songId, stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Fetching the whole song {SongId} failed", songId);
                }
                finally
                {
                    _pending.TryRemove(songId, out _);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        await sweep;
    }

    private async Task SweepLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(SweepEvery);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var swept = await scope.ServiceProvider.GetRequiredService<SongLibrary>().SweepAsync(DateTime.UtcNow, ct);
                    if (swept > 0)
                        logger.LogInformation("Swept {Count} songs nothing uses", swept);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Sweeping unused songs failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
