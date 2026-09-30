using System.Text.RegularExpressions;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// Turns what a model returned into what a person in a chat would have sent.
/// </summary>
public static partial class BotReplyFormatter
{
    /// <summary>
    /// Splits a reply on blank lines into at most <paramref name="maxParts"/>
    /// messages; anything past the limit stays in the last one. Markdown the
    /// prompt asked the model not to use is stripped at the same point.
    /// </summary>
    public static List<string> Split(string reply, int maxParts)
    {
        var parts = BlankLines().Split(reply)
            .Select(StripMarkdown)
            .Where(p => p.Length > 0)
            .ToList();

        if (parts.Count > maxParts && maxParts > 0)
        {
            var tail = string.Join("\n", parts[(maxParts - 1)..]);
            parts = [.. parts[..(maxParts - 1)], tail];
        }

        return parts;
    }

    /// <summary>
    /// Keeps only the bot's own turn. Models that see the history as
    /// "[Name]: text" lines sometimes carry on writing that transcript, putting
    /// words in other people's mouths ("[Alice]: ok got it"). A header at the
    /// very start is the model labelling its own turn and is dropped; the
    /// first header after that ends the reply.
    /// </summary>
    public static string DropOtherSpeakers(string reply)
    {
        reply = reply.Trim();

        var own = SpeakerHeader().Match(reply);
        if (own.Success && own.Index == 0)
            reply = reply[own.Length..].TrimStart();

        var other = SpeakerHeader().Match(reply);
        return other.Success ? reply[..other.Index].TrimEnd() : reply;
    }

    /// <summary>Removes headings, bold/italic markers and list bullets.</summary>
    public static string StripMarkdown(string text)
    {
        text = Heading().Replace(text, "");
        text = Bullet().Replace(text, "");
        text = Emphasis().Replace(text, "$2");
        return text.Trim();
    }

    /// <summary>
    /// True when <paramref name="reply"/> says what one of
    /// <paramref name="previous"/> already said - same words, give or take
    /// punctuation, case and a word or two. Very short replies never count:
    /// people say "lol" twice too.
    /// </summary>
    public static bool IsNearDuplicate(string reply, IEnumerable<string> previous)
    {
        var words = Words(reply);
        if (words.Count < 4)
            return false;

        foreach (var earlier in previous)
        {
            var other = Words(earlier);
            if (other.Count == 0)
                continue;

            var overlap = (double)words.Intersect(other).Count() / words.Union(other).Count();
            if (overlap >= 0.8)
                return true;
        }

        return false;
    }

    private static HashSet<string> Words(string text) =>
        Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToHashSet();

    // The history's line headers: "[Alice]:", "[Alice, 2h later] (→ Bob):".
    [GeneratedRegex(@"^[ \t]*\[[^\]\n]{1,60}\](?:[ \t]*\([^)\n]*\))*[ \t]*:", RegexOptions.Multiline)]
    private static partial Regex SpeakerHeader();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*[-*•]\s+", RegexOptions.Multiline)]
    private static partial Regex Bullet();

    // Bold or italic around text that hugs its markers, so "2 * 3 * 4" survives.
    [GeneratedRegex(@"(\*\*|__|\*)(\S(?:.*?\S)?)\1")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Word();
}
