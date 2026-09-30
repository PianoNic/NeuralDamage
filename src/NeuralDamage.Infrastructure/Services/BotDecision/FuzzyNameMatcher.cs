using System.Text.RegularExpressions;

namespace NeuralDamage.Infrastructure.Services.BotDecision;

public static class FuzzyNameMatcher
{
    /// <summary>Addresses the whole room wherever it appears.</summary>
    private static readonly string[] GroupAddressPatterns =
        ["everyone", "everybody", "@all", "all bots", "you guys", "you all", "yall"];

    /// <summary>
    /// Addresses the room only when asked: "can anyone help?" is, but
    /// "someone told me..." is just a sentence.
    /// </summary>
    private static readonly string[] QuestionOnlyPatterns =
        ["anyone", "anybody", "someone", "somebody"];

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
        // Drop apostrophes so "y'all" and "yall" are the same term.
        var lower = message.ToLowerInvariant().Replace("'", "").Replace("’", "");
        if (GroupAddressPatterns.Any(p => IsWholeWordMatch(lower, p)))
            return true;

        return lower.Contains('?') && QuestionOnlyPatterns.Any(p => IsWholeWordMatch(lower, p));
    }

    private static bool IsWholeWordMatch(string text, string word)
    {
        // Lookarounds rather than \b, so a term starting with a symbol ("@all") still matches.
        var pattern = $@"(?<!\w){Regex.Escape(word)}(?!\w)";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }
}
