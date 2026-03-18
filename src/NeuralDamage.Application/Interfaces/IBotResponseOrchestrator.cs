namespace NeuralDamage.Application.Interfaces;

public interface IBotResponseOrchestrator
{
    Task ProcessMessageAsync(Guid chatId, Guid messageId, CancellationToken ct = default);
    void CancelPendingResponses(Guid chatId);
}

public interface IBotResponseQueue
{
    ValueTask EnqueueAsync(Guid chatId, Guid messageId, CancellationToken ct = default);
}
