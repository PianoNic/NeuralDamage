using System.Text.RegularExpressions;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

public static class FuzzyNameMatcher
{
    private static readonly string[] GroupAddressPatterns = ["everyone", "all bots", "you guys", "you all", "y'all"];

    public static bool IsNameMentioned(string message, string botName, string? aliases)
    {
        return FindNameMatch(message, botName, aliases).Matched;
    }

    public static (bool Matched, string? MatchedTerm) FindNameMatch(string message, string botName, string? aliases)
    {
        if (string.IsNullOrWhiteSpace(message))
            return (false, null);

        var lowerMessage = message.ToLowerInvariant();

        // Check full name first
        if (IsWholeWordMatch(lowerMessage, botName.ToLowerInvariant()))
            return (true, botName);

        // Check individual tokens from the bot name (split on spaces, hyphens, underscores)
        var nameTokens = Regex.Split(botName, @"[\s\-_]+")
            .SelectMany(t => new[] { t, Regex.Replace(t, @"\d+", "") }) // also try without digits
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var token in nameTokens)
        {
            if (IsWholeWordMatch(lowerMessage, token.ToLowerInvariant()))
                return (true, token);
        }

        // Check aliases
        if (!string.IsNullOrWhiteSpace(aliases))
        {
            var aliasList = aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var alias in aliasList)
            {
                if (alias.Length < 2) continue;
                if (IsWholeWordMatch(lowerMessage, alias.ToLowerInvariant()))
                    return (true, alias);
            }
        }

        return (false, null);
    }

    public static bool IsGroupAddress(string message)
    {
        var lower = message.ToLowerInvariant();
        return GroupAddressPatterns.Any(p => lower.Contains(p));
    }

    private static bool IsWholeWordMatch(string text, string word)
    {
        var pattern = $@"\b{Regex.Escape(word)}\b";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }
}
