namespace NeuralDamage.Infrastructure.Services;

public static class BotReactionService
{
    private const double ReactionProbability = 0.15;

    private static readonly Dictionary<string[], string> KeywordEmojiMap = new()
    {
        { ["funny", "lol", "lmao", "haha", "hilarious", "joke"], "😂" },
        { ["love", "amazing", "awesome", "beautiful", "wonderful"], "❤️" },
        { ["agree", "yes", "exactly", "true", "right", "correct"], "👍" },
        { ["wow", "whoa", "incredible", "insane", "unbelievable"], "😮" },
        { ["sad", "sorry", "unfortunate", "tragic", "rip"], "😢" },
        { ["thanks", "thank", "grateful", "appreciate"], "🙏" },
        { ["fire", "lit", "hot", "sick", "beast", "goat"], "🔥" },
        { ["perfect", "flawless", "100", "nailed"], "💯" },
    };

    public static bool ShouldReact() => Random.Shared.NextDouble() < ReactionProbability;

    public static string? SelectEmoji(string messageContent)
    {
        var lower = messageContent.ToLowerInvariant();
        foreach (var (keywords, emoji) in KeywordEmojiMap)
        {
            if (keywords.Any(k => lower.Contains(k)))
                return emoji;
        }

        // Fallback: random common emoji
        var fallbacks = new[] { "👍", "❤️", "😂", "🔥" };
        return fallbacks[Random.Shared.Next(fallbacks.Length)];
    }
}
