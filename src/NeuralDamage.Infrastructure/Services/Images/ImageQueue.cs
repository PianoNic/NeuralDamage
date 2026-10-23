using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace NeuralDamage.Infrastructure.Services.Images;

/// <summary>
/// The image generator has one GPU, so pictures are drawn one at a time, in
/// the order they were asked for. Each gets <see cref="ImageGenOptions.JobTimeout"/>
/// once it is its turn; one that takes longer is cancelled and fails with a
/// <see cref="TimeoutException"/>.
/// </summary>
public sealed class ImageQueue : IDisposable
{
    private readonly Channel<Func<CancellationToken, Task>> _jobs =
        Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeSpan _timeout;
    private int _waiting;

    public ImageQueue(IOptions<ImageGenOptions> options) : this(options.Value.JobTimeout)
    {
    }

    public ImageQueue(TimeSpan timeout)
    {
        _timeout = timeout;
        _ = Task.Run(RunAsync);
    }

    /// <summary>Pictures waiting or being drawn.</summary>
    public int Waiting => Volatile.Read(ref _waiting);

    /// <summary>Runs <paramref name="work"/> when it is its turn, with a token that fires at the timeout.</summary>
    /// <param name="ct">Gives up the job, waiting or running.</param>
    public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Increment(ref _waiting);
        var written = _jobs.Writer.TryWrite(async stop =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop, ct);
            cts.CancelAfter(_timeout);
            try
            {
                ct.ThrowIfCancellationRequested();
                result.TrySetResult(await work(cts.Token));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || stop.IsCancellationRequested)
            {
                result.TrySetCanceled();
            }
            catch (OperationCanceledException)
            {
                result.TrySetException(new TimeoutException($"The picture took longer than {_timeout.TotalMinutes:0.#} minutes."));
            }
            catch (Exception ex)
            {
                result.TrySetException(ex);
            }
        });
        if (!written)
        {
            Interlocked.Decrement(ref _waiting);
            result.TrySetCanceled();
        }
        return result.Task;
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var job in _jobs.Reader.ReadAllAsync(_stop.Token))
            {
                try
                {
                    await job(_stop.Token);
                }
                finally
                {
                    Interlocked.Decrement(ref _waiting);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    public void Dispose()
    {
        _jobs.Writer.TryComplete();
        // Not disposed: the worker may still read its token on the way out.
        _stop.Cancel();
    }
}
