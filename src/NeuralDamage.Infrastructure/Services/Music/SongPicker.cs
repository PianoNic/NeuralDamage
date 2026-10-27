using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Memory;

namespace NeuralDamage.Infrastructure.Services.Music;

/// <summary>
/// Picks the song a bot's post or story plays. Bots don't pick freely, since weak models name
/// songs that don't exist: the server makes a shortlist (songs matching the post's words, then the
/// chart) and the bot answers with a number from it, or names a real song, which has to be found on
/// Deezer. An answer that is neither gets the first song of the list.
/// </summary>
public partial class SongPicker(
    IDeezerClient deezer,
    SongLibrary library,
    IOpenRouterService openRouter,
    FeedPostWriter posts,
    IOptions<MusicOptions> options,
    ILogger<SongPicker> logger)
{
    private readonly MusicOptions _options = options.Value;

    public bool IsEnabled => _options.Enabled;

    /// <summary>Whether this post or story gets a song, from a random number in [0, 1).</summary>
    public bool Wants(double roll) => IsEnabled && roll < _options.BotShare;

    /// <summary>The song for what <paramref name="bot"/> posts, stored; null when there is none to be had.</summary>
    /// <param name="what">What the post or story says and shows.</param>
    public async Task<(Guid SongId, int? Start)?> PickAsync(Bot bot, string what, CancellationToken ct = default)
    {
        var shortlist = await ShortlistAsync(what, ct);
        if (shortlist.Count == 0)
            return null;

        var reply = await BotResponseOrchestrator.GenerateAsync(openRouter, bot.Name, posts.ModelFor(bot), Math.Min(bot.Temperature, 1.0),
            BotPromptBuilder.Character(bot), [new ChatMessage("user", Ask(what, shortlist))], logger, ct);
        var (index, named) = ParseAnswer(reply, shortlist.Count);
        if (index == 0)
            return null;

        var track = index is { } i ? shortlist[i - 1] : null;
        if (track is null && named is { } song)
            track = Matching(await deezer.SearchAsync($"{song.Artist} {song.Title}", 5, ct), song.Artist, song.Title);
        track ??= shortlist[0];

        if (await library.AddAsync(track.Id, wantFull: false, ct) is not { } stored)
            return null;
        logger.LogInformation("Bot {BotName} picked {Title} by {Artist}", bot.Name, stored.Title, stored.Artist);
        return await library.UseAsync(stored.Id, null, ct);
    }

    /// <summary>
    /// The songs a bot picks from: Deezer's best matches for the post's words, then what is popular
    /// now, each only once, at most <see cref="MusicOptions.ShortlistSize"/>.
    /// </summary>
    public async Task<List<DeezerTrack>> ShortlistAsync(string what, CancellationToken ct = default)
    {
        var size = Math.Max(1, _options.ShortlistSize);
        var matching = Keywords(what) is { Length: > 0 } words ? await deezer.SearchAsync(words, size, ct) : [];
        var chart = await deezer.ChartAsync(size, ct);
        return matching.Take((size + 1) / 2).Concat(chart).Concat(matching.Skip((size + 1) / 2))
            .DistinctBy(t => t.Id)
            .Take(size)
            .ToList();
    }

    /// <summary>What the bot is asked.</summary>
    public static string Ask(string what, IReadOnlyList<DeezerTrack> shortlist)
    {
        var songs = string.Join("\n", shortlist.Select((t, i) => $"{i + 1}. {t.Title} - {t.Artist}"));
        return $"""
            You're adding a song to this, which you're about to post:
            {what.Trim()}

            Songs you could add:
            {songs}

            Answer with the number of the one that fits best, the way you'd pick it. If none of them is you, name one real, well-known song that is, as "Artist - Title". Answer 0 for no song. Only the number or the song, nothing else.
            """;
    }

    /// <summary>The bot's answer: a number from the list (0 for no song), or a song it named.</summary>
    public static (int? Index, (string Artist, string Title)? Named) ParseAnswer(string? reply, int count)
    {
        var text = (reply ?? "").Trim().Trim('"', '\'', '*', '.', ' ');
        if (text.Length == 0)
            return (null, null);
        if (LeadingNumber().Match(text) is { Success: true } number && int.TryParse(number.Groups[1].Value, out var n) && n >= 0 && n <= count)
            return (n, null);
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim().Trim('"', '*');
        var dash = line.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && dash < line.Length - 3)
            return (null, (line[..dash].Trim(), line[(dash + 3)..].Trim().Trim('"')));
        return (null, null);
    }

    /// <summary>The first result that really is the song named: its artist and title both match.</summary>
    public static DeezerTrack? Matching(IReadOnlyList<DeezerTrack> results, string artist, string title)
    {
        return results.FirstOrDefault(t => Same(t.Artist, artist) && Same(t.Title, title));

        // Mostly the same words; a name too short to have any must be the same name.
        static bool Same(string found, string named)
        {
            var words = BotMemories.Words(named);
            return words.Count == 0
                ? string.Equals(found.Trim(), named.Trim(), StringComparison.OrdinalIgnoreCase)
                : BotMemories.Overlap(BotMemories.Words(found), words) >= 0.5;
        }
    }

    /// <summary>A few telling words from the post, longest first, for the search.</summary>
    public static string Keywords(string what) =>
        string.Join(' ', BotMemories.Words(what).Where(w => w.Length >= 4 && !w.All(char.IsDigit)).OrderByDescending(w => w.Length).Take(3));

    [GeneratedRegex(@"^#?\s*(\d{1,2})\b")]
    private static partial Regex LeadingNumber();
}
