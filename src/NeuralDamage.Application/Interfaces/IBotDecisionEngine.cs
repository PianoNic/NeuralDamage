using NeuralDamage.Domain;

namespace NeuralDamage.Application.Interfaces;

public interface IBotDecisionEngine
{
    Task<List<Guid>> DecideRespondersAsync(Guid chatId, Message message, List<Bot> candidateBots, CancellationToken ct = default);
}
