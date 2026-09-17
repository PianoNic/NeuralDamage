namespace NeuralDamage.Infrastructure.Services.BotDecision;

/// <summary>
/// The model that ranks which bots should answer a message. Kept separate from
/// <see cref="IOpenRouterService"/> because it sits in the path of every
/// incoming message and is tuned for latency rather than prose quality.
/// </summary>
public interface IBotRankingService
{
    /// <summary>
    /// Returns the model's raw reply, or null when ranking is unavailable and
    /// the caller should fall back to its own heuristic.
    /// </summary>
    Task<string?> RankAsync(string systemPrompt, string prompt, CancellationToken ct = default);
}
