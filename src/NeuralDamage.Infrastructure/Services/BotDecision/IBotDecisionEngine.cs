using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

public enum BotAction
{
    Quiet,
    Reply,
    React,
}

/// <summary>What one bot does with a message: at most one action.</summary>
/// <param name="Emoji">The reaction, for <see cref="BotAction.React"/>.</param>
/// <param name="Probability">Jev's probability for the chosen option; 0 when Jev was not asked.</param>
public record BotVerdict(Bot Bot, BotAction Action, string? Emoji = null, double Probability = 0);

public interface IBotDecisionEngine
{
    /// <summary>
    /// Decides, in one Jev call, what each of <paramref name="bots"/> does with
    /// <paramref name="message"/>. The caller has already left out the bots
    /// that must stay quiet (muted, stopped, the sender).
    /// </summary>
    Task<List<BotVerdict>> DecideAsync(Guid chatId, Message message, List<Bot> bots, CancellationToken ct = default);
}
