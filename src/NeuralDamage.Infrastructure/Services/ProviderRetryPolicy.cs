using System.ClientModel.Primitives;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// Retries a generation the provider turned away for now: rate limited (429),
/// or a gateway or the provider overloaded (502, 503, 529). Two retries, with
/// a doubling backoff, or the wait the provider asks for in Retry-After when
/// that is longer. A provider asking for more than <see cref="MaxWait"/> is not
/// retried: a chat reply that late is no longer worth waiting for.
/// </summary>
public sealed class ProviderRetryPolicy(TimeSpan baseDelay, ILogger logger) : ClientRetryPolicy(MaxRetries)
{
    public const int MaxRetries = 2;
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(10);

    public static bool IsRetried(int status) => status is 429 or 502 or 503 or 529;

    protected override bool ShouldRetry(PipelineMessage message, Exception? exception)
    {
        // A network failure is judged by the SDK as before.
        if (exception is not null)
            return base.ShouldRetry(message, exception);

        var retries = message.TryGetProperty(typeof(ProviderRetryPolicy), out var count) ? (int)count! : 0;
        if (retries >= MaxRetries || message.Response is not { } response || !IsRetried(response.Status))
            return false;
        if (RetryAfter(response) > MaxWait)
            return false;

        message.SetProperty(typeof(ProviderRetryPolicy), retries + 1);
        logger.LogWarning("OpenRouter answered {Status}; retry {Retry} of {MaxRetries}", response.Status, retries + 1, MaxRetries);
        return true;
    }

    protected override ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception) =>
        exception is not null ? base.ShouldRetryAsync(message, exception) : ValueTask.FromResult(ShouldRetry(message, exception));

    protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount)
    {
        var backoff = baseDelay * Math.Pow(2, Math.Max(tryCount - 1, 0));
        var asked = message.Response is { } response ? RetryAfter(response) : null;
        return asked > backoff ? asked.Value : backoff;
    }

    /// <summary>Retry-After in seconds or as a date, or OpenRouter's retry-after-ms.</summary>
    public static TimeSpan? RetryAfter(PipelineResponse response)
    {
        if (response.Headers.TryGetValue("retry-after-ms", out var ms)
            && double.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
            return TimeSpan.FromMilliseconds(Math.Max(milliseconds, 0));
        if (!response.Headers.TryGetValue("Retry-After", out var value) || value is null)
            return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return TimeSpan.FromSeconds(Math.Max(seconds, 0));
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            return at > DateTimeOffset.UtcNow ? at - DateTimeOffset.UtcNow : TimeSpan.Zero;
        return null;
    }
}

/// <summary>
/// The provider was still rate limited or overloaded after the retries. Its
/// text is one line, without the stack trace, since a busy provider is not a
/// bug and the trace would only bury the log.
/// </summary>
public sealed class ProviderBusyException(string modelId, int status, Exception inner)
    : Exception($"OpenRouter answered {status} for '{modelId}' after {ProviderRetryPolicy.MaxRetries} retries.", inner)
{
    public int Status { get; } = status;

    public override string ToString() => $"{GetType().FullName}: {Message}";
}
