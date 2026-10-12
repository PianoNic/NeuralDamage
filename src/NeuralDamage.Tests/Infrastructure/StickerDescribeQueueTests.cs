using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Infrastructure.Services.Stickers;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>A big import is described a few stickers at a time, with a fake describer.</summary>
public class StickerDescribeQueueTests
{
    [Test]
    public async Task NeverDescribesMoreThanFourAtOnce_AndGetsThroughThemAll()
    {
        var running = 0;
        var peak = 0;
        var done = new List<Guid>();
        var allDone = new TaskCompletionSource();
        const int count = 40;

        async Task Describe(Guid id, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref running);
            lock (done)
                peak = Math.Max(peak, now);
            await Task.Delay(15, ct);
            Interlocked.Decrement(ref running);
            lock (done)
            {
                done.Add(id);
                if (done.Count == count)
                    allDone.TrySetResult();
            }
        }

        var queue = new StickerDescribeQueue(Describe, NullLogger<StickerDescribeQueue>.Instance);
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
            queue.Enqueue(id);
        await Assert.That(queue.Pending).IsEqualTo(count);

        using var cts = new CancellationTokenSource();
        var run = queue.RunAsync(cts.Token);
        await allDone.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(queue.Parallelism).IsEqualTo(4);
        await Assert.That(peak).IsEqualTo(4);
        await Assert.That(done).IsEquivalentTo(ids);

        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(queue.Pending).IsEqualTo(0);
    }

    [Test]
    public async Task AFailedDescription_DoesNotStopTheQueue()
    {
        var described = new TaskCompletionSource<Guid>();
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        var queue = new StickerDescribeQueue((id, _) =>
        {
            if (id == bad)
                throw new HttpRequestException("401 Unauthorized");
            described.TrySetResult(id);
            return Task.CompletedTask;
        }, NullLogger<StickerDescribeQueue>.Instance, parallelism: 1);

        queue.Enqueue(bad);
        queue.Enqueue(good);
        using var cts = new CancellationTokenSource();
        var run = queue.RunAsync(cts.Token);

        await Assert.That(await described.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(good);
        await cts.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(queue.Pending).IsEqualTo(0);
    }
}
