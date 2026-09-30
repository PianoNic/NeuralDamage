using Microsoft.Extensions.Logging;

namespace NeuralDamage.Tests.Helpers;

/// <summary>Keeps every formatted log line with its level, for asserting on what was logged.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
