using System.Text.RegularExpressions;
using NeuralDamage.Infrastructure.Services.Stickers;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>One message a reply turns into: text, or a sticker.</summary>
public record ReplyPart(string Text, BotSticker? Sticker = null);

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
            .Select(StripDashes)
            .Select(p => RepeatedSymbol().Replace(p, "$1$1$1"))
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
    /// Cleans a model's reply into what would be posted: its own name prefix,
    /// any lines written for other people and any invented "[image from ...]"
    /// lines go, then it is split into at most <paramref name="maxParts"/>,
    /// each "[sticker: name]" becomes a sticker part (or nothing, for a name
    /// <paramref name="findSticker"/> does not know), and parts that are only
    /// a stage direction or punctuation are dropped. The same for chat
    /// messages, feed posts and feed comments.
    /// </summary>
    public static List<ReplyPart> ToParts(string reply, string botName, int maxParts, Func<string, BotSticker?> findSticker) =>
        ExtractStickers(Split(StripImageLines(DropOtherSpeakers(StripOwnName(reply, botName))), maxParts), findSticker)
            .Where(p => p.Sticker is not null || !IsFiller(p.Text))
            .ToList();

    /// <summary>
    /// One post or comment from a reply: its text parts on lines of their own,
    /// and the first sticker it named. Empty text and no sticker means nothing to post.
    /// </summary>
    public static (string Text, BotSticker? Sticker) Single(List<ReplyPart> parts, int maxLength)
    {
        var text = string.Join("\n", parts.Where(p => p.Sticker is null).Select(p => p.Text)).Trim();
        if (text.Length > maxLength)
            text = text[..maxLength].TrimEnd();
        return (text, parts.FirstOrDefault(p => p.Sticker is not null)?.Sticker);
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

    /// <summary>
    /// Drops the bot's own name when the model opens its reply with it:
    /// "Gus: hi", "[Gus]: hi", "**Gus:** hi", or the name on a line of its own -
    /// which some models also repeat before each paragraph.
    /// </summary>
    public static string StripOwnName(string reply, string botName)
    {
        var name = Regex.Escape(botName);
        var prefix = new Regex($@"^\s*(?:\*\*|__)?\[?{name}\]?(?:\*\*|__)?[ \t]*:(?:\*\*|__)?\s*", RegexOptions.IgnoreCase);
        var headerLine = new Regex($@"^[ \t]*(?:\*\*|__)?\[?{name}\]?(?:\*\*|__)?[ \t]*:(?:\*\*|__)?[ \t]*(?:\r?\n|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return headerLine.Replace(prefix.Replace(reply, "", 1), "");
    }

    /// <summary>
    /// Drops lines that are only "[image from Alice]" or "[image from Alice: ...]",
    /// or the same for stickers. That is how the history shows them, and some
    /// models imitate it, inventing images nobody sent. A reply of nothing else
    /// ends up empty.
    /// </summary>
    public static string StripImageLines(string reply) => ImageLine().Replace(reply, "").Trim();

    /// <summary>
    /// Pulls "[sticker: name]" out of the reply's parts: each one the bot named
    /// that <paramref name="find"/> knows becomes a sticker message of its own,
    /// in place; unknown names are dropped without a trace. Text around a
    /// sticker in the same part stays, before and after it.
    /// </summary>
    public static List<ReplyPart> ExtractStickers(IEnumerable<string> parts, Func<string, BotSticker?> find)
    {
        var result = new List<ReplyPart>();
        foreach (var part in parts)
        {
            var at = 0;
            foreach (Match token in StickerToken().Matches(part))
            {
                AddText(result, part[at..token.Index]);
                if (find(token.Groups[1].Value.Trim()) is { } sticker)
                    result.Add(new ReplyPart("", sticker));
                at = token.Index + token.Length;
            }
            AddText(result, part[at..]);
        }
        return result;

        static void AddText(List<ReplyPart> into, string text)
        {
            text = text.Trim();
            if (text.Length > 0)
                into.Add(new ReplyPart(text, null));
        }
    }

    /// <summary>
    /// Replaces em dashes, and en dashes or bars used the same way, with a
    /// comma: nobody texts them, and they give a bot away as a model. The prompt
    /// asks for none, but models slip, so it is enforced here. Hyphens and
    /// ranges ("e-mail", "2-3", "2–3") stay.
    /// </summary>
    public static string StripDashes(string text)
    {
        text = Dash().Replace(text, ", ");
        text = CommaBeforePunctuation().Replace(text, "$1");
        return StrayComma().Replace(text, "").Trim();
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

    /// <summary>
    /// True for a message nobody would send: only a stage direction in
    /// parentheses ("(ignores bob completely)") or only punctuation ("...").
    /// </summary>
    public static bool IsFiller(string part) =>
        StageDirection().IsMatch(part) || OnlyPunctuation().IsMatch(part);

    private static HashSet<string> Words(string text) =>
        Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToHashSet();

    // The history's line headers: "[Alice]:", "[Alice, 2h later] (→ Bob):".
    [GeneratedRegex(@"^[ \t]*\[[^\]\n]{1,60}\](?:[ \t]*\([^)\n]*\))*[ \t]*:", RegexOptions.Multiline)]
    private static partial Regex SpeakerHeader();

    // A whole line in the history's picture format, with its line break.
    [GeneratedRegex(@"^[ \t]*\[(?:image|sticker) from\b[^\n]*\][ \t]*(?:\r?\n|$)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ImageLine();

    // "[sticker: name]", wherever the model put it.
    [GeneratedRegex(@"\[sticker:\s*([^\]\n]{1,64})\]", RegexOptions.IgnoreCase)]
    private static partial Regex StickerToken();

    // A symbol repeated past three ("!!!!!!!!"): what a model that has
    // degenerated sends, hundreds of times over. Nobody types more than three.
    [GeneratedRegex(@"([^\p{L}\p{N}\s])\1{3,}")]
    private static partial Regex RepeatedSymbol();

    // Any em dash or horizontal bar, or an en dash with a space on either side.
    [GeneratedRegex(@"[ \t]*[—―][ \t]*|[ \t]+–[ \t]*|[ \t]*–[ \t]+")]
    private static partial Regex Dash();

    [GeneratedRegex(@",[ \t]*([.,!?…])")]
    private static partial Regex CommaBeforePunctuation();

    // A comma the dash left at the start or end of a line.
    [GeneratedRegex(@"^[ \t]*,[ \t]*|[ \t]*,[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex StrayComma();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*[-*•]\s+", RegexOptions.Multiline)]
    private static partial Regex Bullet();

    // Bold or italic around text that hugs its markers, so "2 * 3 * 4" survives.
    [GeneratedRegex(@"(\*\*|__|\*)(\S(?:.*?\S)?)\1")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"^(?:\s*\([^()]*\)\s*)+$")]
    private static partial Regex StageDirection();

    [GeneratedRegex(@"^[\p{P}\s]*$")]
    private static partial Regex OnlyPunctuation();

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Word();
}
