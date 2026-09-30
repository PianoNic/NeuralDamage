namespace NeuralDamage.Infrastructure.Services;

public interface IBotResponseOrchestrator
{
    /// <param name="depth">
    /// Bot-to-bot hops behind this message: 0 for a person's message, one more
    /// for each bot reply in the chain.
    /// </param>
    Task ProcessMessageAsync(Guid chatId, Guid messageId, int depth = 0, CancellationToken ct = default);
    void CancelPendingResponses(Guid chatId);
}

public interface IBotResponseQueue
{
    ValueTask EnqueueAsync(Guid chatId, Guid messageId, CancellationToken ct = default);

    /// <summary>Queues a bot's own message, <paramref name="depth"/> hops into a bot-to-bot chain.</summary>
    ValueTask EnqueueAsync(Guid chatId, Guid messageId, int depth, CancellationToken ct = default);
}
