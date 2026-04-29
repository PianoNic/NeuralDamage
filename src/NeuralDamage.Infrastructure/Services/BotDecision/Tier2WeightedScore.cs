namespace NeuralDamage.Infrastructure.Services.BotDecision;

public record Tier2Context(
    bool IsGroupQuestion,
    int BotMessagesInLast20,
    int TotalRecentMessages,
    int SecondsSinceLastBotMessage,
    int MessageLength,
    int TotalBotsInChat);

public static class Tier2WeightedScore
{
    public const double RespondThreshold = 0.6;
    public const double SkipThreshold = 0.15;

    public static double ComputeScore(Tier2Context context)
    {
        double score = 0.15; // base interest

        // Group question bonus
        if (context.IsGroupQuestion)
            score += 0.25;

        // Recency penalty
        if (context.SecondsSinceLastBotMessage >= 0)
        {
            if (context.SecondsSinceLastBotMessage < 30)
                score -= 0.4;
            else if (context.SecondsSinceLastBotMessage < 120)
                score -= 0.2;
        }

        // Dominance penalty
        if (context.TotalRecentMessages > 0)
        {
            var ratio = (double)context.BotMessagesInLast20 / context.TotalRecentMessages;
            if (ratio > 0.4)
                score -= 0.15;
        }

        // Short message penalty
        if (context.MessageLength < 10)
            score -= 0.1;

        // Many bots penalty (each extra bot reduces chance)
        var botPenalty = Math.Min(0.15, 0.05 * (context.TotalBotsInChat - 1));
        score -= botPenalty;

        return Math.Clamp(score, 0.0, 1.0);
    }
}
