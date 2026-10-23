using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

public enum BotAction
{
    Quiet,
    Reply,
    React,
}

/// <summary>What one bot does with a message: reply, react, or both.</summary>
/// <param name="Emoji">
/// The reaction: always set for <see cref="BotAction.React"/>, and set for
/// <see cref="BotAction.Reply"/> when the bot also reacts to what it answers.
/// </param>
/// <param name="Probability">Jev's probability for the chosen option; 0 when Jev was not asked.</param>
/// <param name="ReactProbability">Jev's probabilities of all the react options together.</param>
public record BotVerdict(Bot Bot, BotAction Action, string? Emoji = null, double Probability = 0, double ReactProbability = 0)
{
    /// <summary>The bot puts <see cref="Emoji"/> on the message.</summary>
    public bool Reacts => Action != BotAction.Quiet && Emoji is not null;
}

public interface IBotDecisionEngine
{
    /// <summary>
    /// Decides, in one Jev call, what each of <paramref name="bots"/> does with
    /// <paramref name="message"/>. The caller has already left out the bots
    /// that must stay quiet (muted, stopped, the sender).
    /// </summary>
    Task<List<BotVerdict>> DecideAsync(Guid chatId, Message message, List<Bot> bots, CancellationToken ct = default);

    /// <summary>
    /// The same decision anywhere else, such as a feed thread: the caller builds
    /// what Jev reads, keyed by the names it used for the bots in its state.
    /// </summary>
    Task<List<BotVerdict>> DecideAsync(DecisionRound round, List<(string Key, Bot Bot)> keyed, CancellationToken ct = default);
}
