using System.Text.RegularExpressions;

namespace NeuralDamage.Infrastructure.Services;

public enum PeopleLanguage
{
    Unknown,
    German,
    English,
}

/// <summary>
/// Works out which language the people in a chat write, from their last few
/// messages, by counting common words. Bots answer each other, and many have
/// English characters, so one English bot reply used to be enough for the next
/// bot to answer in English while every person wrote German. The people's
/// language is what the chat speaks; the bots follow it.
/// </summary>
/// <remarks>
/// No model call: a stopword count is cheap and good enough for the two
/// languages the app is used in. Swiss German counts as German. Words both
/// languages use ("was", "so", "in", "will", "man") count for neither.
/// </remarks>
public static partial class ChatLanguage
{
    /// <summary>How many of the people's latest messages are looked at.</summary>
    public const int MessagesLooked = 8;

    /// <summary>Fewer hits than this is too little text to tell.</summary>
    private const int MinHits = 2;

    private static readonly HashSet<string> German =
    [
        // Standard German
        "ich", "du", "er", "sie", "wir", "ihr", "mich", "dich", "mir", "dir", "uns", "euch", "mein", "dein", "meine", "deine",
        "und", "oder", "aber", "nicht", "kein", "keine", "auch", "noch", "schon", "nur", "sehr", "doch", "mal", "halt", "eben",
        "der", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "einer", "ist", "sind", "bin", "bist", "hast", "habe",
        "haben", "hatte", "wird", "werden", "kann", "kannst", "können", "muss", "musst", "soll", "will", "willst", "gibt",
        "dass", "wenn", "weil", "wie", "wer", "wo", "warum", "wieso", "heute", "morgen", "gestern", "jetzt", "hier", "dort",
        "ja", "nein", "gut", "danke", "bitte", "mit", "für", "von", "zu", "zum", "zur", "auf", "aus", "bei", "nach", "über",
        "unter", "vielleicht", "gerade", "immer", "nie", "nichts", "etwas", "alles", "viel", "mehr", "weiß", "glaube",
        // Swiss German
        "isch", "bisch", "hesch", "hett", "het", "han", "hän", "gsi", "gseh", "nöd", "ned", "nid", "öppis", "öpper", "öppe",
        "chasch", "cha", "chan", "chunsch", "chunt", "wotsch", "wett", "gang", "gah", "mer", "eus", "öich", "dä", "de",
        "zit", "morn", "hüt", "geschter", "din", "dini", "min", "mini", "sin", "sini", "au", "scho", "no", "jo", "nei", "guet",
        "merci", "grüezi", "gell", "uf", "vo", "worum", "wänn", "kei", "keis", "es", "gits",
        "hani", "bini", "hemmer", "simmer", "lueg", "chli", "bitzli", "eifach", "sicher", "voll",
    ];

    private static readonly HashSet<string> English =
    [
        "the", "and", "is", "are", "you", "i", "to", "of", "a", "it", "that", "this", "what", "with", "for", "on", "do",
        "does", "did", "have", "has", "be", "been", "not", "my", "your", "me", "we", "they", "he", "she", "but", "just",
        "can", "can't", "how", "why", "there", "at", "it's", "don't", "i'm", "you're", "yeah", "yes", "no", "about",
        "would", "should", "could", "really", "think", "know", "like", "get", "got", "going", "want", "some", "any",
        "from", "or", "if", "then", "than", "when", "where", "who", "which", "today", "tomorrow", "yesterday", "now",
        "here", "please", "thanks", "thank", "good", "great", "lol", "anyone", "someone", "something", "nothing", "never",
        "always", "maybe", "sure", "okay", "too", "very", "much", "more", "here's", "that's", "what's", "isn't",
    ];

    static ChatLanguage()
    {
        // Words both languages use say nothing about which one this is.
        foreach (var shared in German.Intersect(English).ToList())
        {
            German.Remove(shared);
            English.Remove(shared);
        }
    }

    /// <summary>The language of <paramref name="texts"/>, oldest first, or Unknown when they don't clearly say.</summary>
    public static PeopleLanguage Detect(IEnumerable<string> texts)
    {
        int german = 0, english = 0;
        foreach (var word in texts.SelectMany(t => Word().Matches(t.ToLowerInvariant())).Select(m => m.Value.Trim('\'')))
        {
            if (German.Contains(word) || word.Any(c => c is 'ä' or 'ö' or 'ü' or 'ß'))
                german++;
            else if (English.Contains(word))
                english++;
        }

        if (german >= MinHits && german >= 2 * english)
            return PeopleLanguage.German;
        if (english >= MinHits && english >= 2 * german)
            return PeopleLanguage.English;
        return PeopleLanguage.Unknown;
    }

    /// <summary>
    /// The language of the people's latest messages in <paramref name="messages"/>
    /// (chronological). Bots' messages don't count.
    /// </summary>
    public static PeopleLanguage OfPeople(IEnumerable<Domain.Message> messages) =>
        Detect(messages.Where(m => m.SenderUserId is not null).TakeLast(MessagesLooked).Select(m => m.Content));

    [GeneratedRegex(@"[\p{L}']+")]
    private static partial Regex Word();
}
