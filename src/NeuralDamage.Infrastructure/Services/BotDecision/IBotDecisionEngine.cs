using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

public interface IBotDecisionEngine
{
    Task<List<Guid>> DecideRespondersAsync(Guid chatId, Message message, List<Bot> candidateBots, CancellationToken ct = default);
}
