using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Infrastructure.Services.Stickers;

/// <summary>
/// Stickers waiting for their one-line description. An import of thousands
/// goes through here a few at a time instead of flooding the describer.
/// </summary>
public interface IStickerDescribeQueue
{
    void Enqueue(Guid stickerId);

    /// <summary>Queued or under way.</summary>
    int Pending { get; }
}

/// <summary>
/// Runs <c>describe</c> for each queued sticker, at most <see cref="Parallelism"/>
/// at a time, in the order they came. A failure is logged and the queue moves on.
/// </summary>
public sealed class StickerDescribeQueue(Func<Guid, CancellationToken, Task> describe, ILogger<StickerDescribeQueue> logger, int parallelism = StickerDescribeQueue.DefaultParallelism)
    : IStickerDescribeQueue
{
    public const int DefaultParallelism = 4;

    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private int _pending;

    public int Parallelism { get; } = Math.Max(1, parallelism);

    public int Pending => Volatile.Read(ref _pending);

    public void Enqueue(Guid stickerId)
    {
        Interlocked.Increment(ref _pending);
        _queue.Writer.TryWrite(stickerId);
    }

    /// <summary>Works the queue until <paramref name="ct"/> is cancelled.</summary>
    public Task RunAsync(CancellationToken ct) =>
        Task.WhenAll(Enumerable.Range(0, Parallelism).Select(_ => WorkAsync(ct)));

    private async Task WorkAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var id in _queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await describe(id, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Describing sticker {StickerId} failed", id);
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
}

/// <summary>
/// Works the <see cref="StickerDescribeQueue"/>. At start it first fills in the
/// hash of stickers from before hashes were kept, then queues again the ones a
/// restart cut short.
/// </summary>
public sealed class StickerDescribeService(StickerDescribeQueue queue, IServiceScopeFactory scopeFactory, ILogger<StickerDescribeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            await BackfillHashesAsync(db, scope.ServiceProvider.GetRequiredService<IAttachmentStorage>(), logger, stoppingToken);

            var unfinished = await db.Stickers
                .Where(s => s.DeletedAt == null && s.Describing && s.Description == null)
                .OrderBy(s => s.CreatedAt)
                .Select(s => s.Id)
                .ToListAsync(stoppingToken);
            foreach (var id in unfinished)
                queue.Enqueue(id);
            if (unfinished.Count > 0)
                logger.LogInformation("Queued {Count} stickers a restart left undescribed", unfinished.Count);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Preparing the sticker describe queue failed");
        }

        await queue.RunAsync(stoppingToken);
    }

    /// <summary>Hashes every sticker that has no hash yet, a hundred at a time.</summary>
    public static async Task BackfillHashesAsync(NeuralDamageDbContext db, IAttachmentStorage storage, ILogger logger, CancellationToken ct)
    {
        var done = 0;
        var missing = new HashSet<Guid>();
        while (true)
        {
            var batch = await db.Stickers
                .Where(s => s.Sha256 == null && !missing.Contains(s.Id))
                .OrderBy(s => s.Id)
                .Take(100)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;
            foreach (var sticker in batch)
            {
                if (await storage.ReadStickerAsync(sticker.Id, ct) is { } data)
                    sticker.Sha256 = StickerRules.Hash(data);
                else
                    missing.Add(sticker.Id);
            }
            await db.SaveChangesAsync(ct);
            done += batch.Count;
        }
        if (done > 0)
            logger.LogInformation("Hashed {Count} stickers", done - missing.Count);
    }
}
