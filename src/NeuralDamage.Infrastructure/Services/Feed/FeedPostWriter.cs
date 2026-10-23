using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Services.Stickers;

namespace NeuralDamage.Infrastructure.Services.Feed;

/// <summary>What a bot is about to post.</summary>
/// <param name="Image">The picture the bot asked for with it, when it was offered one.</param>
public record FeedDraft(string Content, BotSticker? Sticker, Guid? SourceChatId, PostImageAsk? Image = null);

/// <summary>A chat a post may react to: its summary and its latest messages.</summary>
public record ChatMaterial(Guid ChatId, string Name, string? Summary, List<Message> Messages);

/// <summary>
/// Writes a bot's own post: in character, from what happened lately in one of
/// its chats or from nothing in particular, after what is already on the feed,
/// at the time of day it is where the app lives. It reacts to a chat but never
/// quotes it: a post that repeats a run of words from it is written again, and
/// dropped if it does it twice.
/// </summary>
public partial class FeedPostWriter(
    NeuralDamageDbContext db,
    IOpenRouterService openRouter,
    IConfiguration configuration,
    ILogger<FeedPostWriter> logger,
    IOptions<FeedOptions>? options = null,
    BotClock? clock = null)
{
    /// <summary>Messages of the chat a post reacts to that the model reads.</summary>
    public const int ChatMessages = 12;

    /// <summary>Feed posts the model reads, so it does not say what was just said.</summary>
    public const int FeedPosts = 8;

    /// <summary>This many words in a row from a chat message make a post a quote.</summary>
    public const int QuoteRun = 6;

    private const int MaxMessageChars = 300;

    private readonly FeedOptions _options = options?.Value ?? new();
    private readonly BotClock _clock = clock ?? BotClock.Default;

    /// <summary>The model posts are written with: <c>Feed:Model</c>, then <c>App:DefaultModel</c>, then the bot's own.</summary>
    public string ModelFor(Bot bot) =>
        NullIfBlank(_options.Model) ?? NullIfBlank(configuration["App:DefaultModel"]) ?? bot.ModelId;

    /// <param name="roll">A random number in [0, 1): whether this post reacts to a chat, and to which.</param>
    /// <param name="offerImage">The bot may add a picture to the post (<see cref="FeedPostOutput"/>): one it composes, or one drawn when <paramref name="canGenerate"/>.</param>
    /// <param name="canGenerate">The image generator is set up.</param>
    /// <param name="canSearch">The image web search is set up.</param>
    public async Task<FeedDraft?> WriteAsync(Bot bot, DateTimeOffset now, double roll, CancellationToken ct = default, bool offerImage = false, bool canGenerate = false, bool canSearch = false)
    {
        var material = roll < _options.ChatInspiredShare ? await MaterialAsync(bot, now, roll, ct) : null;
        var recent = await RecentPostsAsync(ct);
        var stickers = await StickerCatalog.LoadAsync(db, ct);
        var language = material is null ? PeopleLanguage.Unknown : ChatLanguage.OfPeople(material.Messages);

        var system = SystemPrompt(bot, stickers.ForPrompt);
        var imageNote = offerImage ? FeedPostOutput.Instruction(canIncludeMe: bot.AvatarUrl is not null, canGenerate, canSearch) : null;
        var turns = Turns(bot, material, recent, now, _clock, language, imageNote);
        var model = ModelFor(bot);
        var temperature = Math.Min(bot.Temperature, 1.2);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var reply = await BotResponseOrchestrator.GenerateAsync(openRouter, bot.Name, model, temperature, system, turns, logger, ct);
            if (string.IsNullOrWhiteSpace(reply))
                return null;

            var (written, image) = offerImage ? FeedPostOutput.Parse(reply) : (reply, null);
            if (offerImage && image is null && reply.Contains("\"image\"", StringComparison.OrdinalIgnoreCase) && !reply.Contains("\"image\": null", StringComparison.OrdinalIgnoreCase) && !reply.Contains("\"image\":null", StringComparison.OrdinalIgnoreCase))
                logger.LogInformation("Bot {BotName} asked for a picture the app could not read: {Reply}", bot.Name, reply.Length > 600 ? reply[..600] : reply);
            var (text, sticker) = BotReplyFormatter.Single(BotReplyFormatter.ToParts(written, bot.Name, 3, stickers.Find), Post.MaxContentLength);
            text = Unquote(text);
            if (text.Length == 0 && sticker is null)
                return null;

            if (material is null || !QuotesChat(text, material.Messages.Select(m => m.Content)))
                return new FeedDraft(text, sticker, material?.ChatId, image);

            logger.LogInformation("Bot {BotName}'s post quoted its chat {ChatId} (attempt {Attempt} of 2)", bot.Name, material.ChatId, attempt);
            turns = [.. turns[..^1], Note(bot, material, now, _clock, language,
                "Your last try repeated what someone said in the chat word for word. Say it in your own words this time, as your reaction, never as a quote."
                + (imageNote is null ? "" : $" {imageNote}"))];
        }

        return null;
    }

    /// <summary>
    /// The character first, as in a chat, then where the bot is and the same
    /// house rules, then the stickers it can add.
    /// </summary>
    public static string SystemPrompt(Bot bot, IReadOnlyList<BotSticker>? stickers)
    {
        var prompt = $"""
            {BotPromptBuilder.Character(bot)}

            You are "{bot.Name}", about to post on the feed of a chat app: a public timeline that everyone using the app reads, people and other characters alike.

            {BotPromptBuilder.HouseRules(bot.Name)}
            """;
        return BotPromptBuilder.WithStickers(prompt, stickers);
    }

    /// <summary>
    /// What the bot reads before it posts: the chat it may react to, marked as
    /// private, then what is already on the feed, then the note that asks for the post.
    /// </summary>
    public static List<ChatMessage> Turns(Bot bot, ChatMaterial? material, List<(string Author, string Content)> recentPosts, DateTimeOffset now, BotClock clock, PeopleLanguage language, string? extra = null)
    {
        var turns = new List<ChatMessage>();
        if (material is not null)
        {
            // People are never named on the feed, so the model never learns their names here.
            var people = material.Messages.Where(m => m.SenderUserId != null).Select(m => m.SenderUserId!.Value).Distinct()
                .Select((id, i) => (id, Label: $"Person {(char)('A' + i % 26)}")).ToDictionary(p => p.id, p => p.Label);
            var lines = material.Messages.Select(m =>
                $"[{(m.SenderUserId is { } userId ? people[userId] : SenderName(m))}]: {Cut(Plain(m), MaxMessageChars)}");
            var summary = string.IsNullOrWhiteSpace(material.Summary) ? "" : $"Earlier: {material.Summary.Trim()}\n\n";
            turns.Add(new ChatMessage("user",
                $"(Your private chat \"{material.Name}\". It is for you only: never quote it.\n\n{summary}Lately:\n{string.Join("\n", lines)})"));
        }

        if (recentPosts.Count > 0)
            turns.Add(new ChatMessage("user",
                $"(On the feed right now:\n{string.Join("\n", recentPosts.Select(p => $"[{p.Author}]: {Cut(p.Content, MaxMessageChars)}"))})"));

        turns.Add(Note(bot, material, now, clock, language, extra));
        return turns;
    }

    private static ChatMessage Note(Bot bot, ChatMaterial? material, DateTimeOffset now, BotClock clock, PeopleLanguage language, string? extra = null)
    {
        var about = material is null
            ? "Post about whatever is on your mind right now, true to who you are."
            : $"React to what happened in your chat \"{material.Name}\": your take on it, how it left you, a joke about it. Never quote anyone from it, never repeat what was said word for word, never name the people in it, and never give away anything private.";
        var lang = language == PeopleLanguage.Unknown ? "" : $" Write in {language}.";
        var more = string.IsNullOrWhiteSpace(extra) ? "" : $" {extra}";
        return new ChatMessage(ChatMessage.Note,
            $"(It is {clock.Describe(now)}. {BotPromptBuilder.Reminder(bot)} Write one post for the feed now, the length of a real post: a line or two. {about} Don't repeat what is already on the feed. Write only the post itself.{lang}{more})");
    }

    /// <summary>
    /// True when <paramref name="post"/> has <see cref="QuoteRun"/> words in a
    /// row that one of <paramref name="chatTexts"/> has too, ignoring case and
    /// punctuation: a quote, not a reaction.
    /// </summary>
    public static bool QuotesChat(string post, IEnumerable<string> chatTexts, int run = QuoteRun)
    {
        var words = Words(post);
        if (words.Count < run)
            return false;
        var runs = Runs(words, run).ToHashSet();
        return chatTexts.Any(text => Runs(Words(text), run).Any(runs.Contains));
    }

    /// <summary>
    /// The chat the post reacts to: one of the bot's chats where a person
    /// wrote lately, picked by <paramref name="roll"/>, with its summary and
    /// its latest messages. Null when it has none.
    /// </summary>
    public async Task<ChatMaterial?> MaterialAsync(Bot bot, DateTimeOffset now, double roll, CancellationToken ct = default)
    {
        var since = now.UtcDateTime - _options.ChatLookback;
        var chats = await db.ChatMembers
            .Where(cm => cm.BotId == bot.Id)
            .Select(cm => cm.Chat)
            .Where(c => c.Messages.Any(m => m.SenderUserId != null && m.CreatedAt >= since))
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Name, c.Summary })
            .AsNoTracking()
            .ToListAsync(ct);
        if (chats.Count == 0)
            return null;

        var share = Math.Max(_options.ChatInspiredShare, double.Epsilon);
        var chat = chats[Math.Min(chats.Count - 1, (int)(roll / share * chats.Count))];
        var messages = await db.Messages
            .Where(m => m.ChatId == chat.Id && !m.Content.StartsWith("/"))
            .OrderByDescending(m => m.CreatedAt)
            .Take(ChatMessages)
            .Include(m => m.SenderUser)
            .Include(m => m.SenderBot)
            .Include(m => m.Attachments)
            .Include(m => m.Sticker)
            .AsNoTracking()
            .ToListAsync(ct);
        messages.Reverse();
        return new ChatMaterial(chat.Id, chat.Name, chat.Summary, messages);
    }

    private async Task<List<(string Author, string Content)>> RecentPostsAsync(CancellationToken ct)
    {
        var posts = await db.Posts.Visible()
            .OrderByDescending(p => p.CreatedAt)
            .Take(FeedPosts)
            .Include(p => p.AuthorUser)
            .Include(p => p.AuthorBot)
            .Include(p => p.Image)
            .AsNoTracking()
            .ToListAsync(ct);
        posts.Reverse();
        return posts.Select(p => (FeedReads.Author(p.AuthorUser, p.AuthorBot).Name, FeedReads.WithPicture(p.Content, p.Image))).ToList();
    }

    /// <summary>Some models wrap the whole post in quotation marks.</summary>
    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] is '"' or '“' && text[^1] is '"' or '”' ? text[1..^1].Trim() : text;

    private static List<string> Words(string text) =>
        Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();

    private static IEnumerable<string> Runs(List<string> words, int run)
    {
        for (var i = 0; i + run <= words.Count; i++)
            yield return string.Join(' ', words.Skip(i).Take(run));
    }

    /// <summary>A message's text with its pictures and sticker as what they show, without who sent them.</summary>
    private static string Plain(Message m)
    {
        var parts = new List<string>();
        if (m.Content.Length > 0)
            parts.Add(m.Content);
        parts.AddRange(m.Attachments.Select(a => string.IsNullOrWhiteSpace(a.Description) ? "[a picture]" : $"[a picture: {a.Description.Trim()}]"));
        if (m.Sticker is { } sticker)
            parts.Add(string.IsNullOrWhiteSpace(sticker.Description) ? "[a sticker]" : $"[a sticker: {sticker.Description.Trim()}]");
        return string.Join("\n", parts);
    }

    private static string SenderName(Message m) => m.SenderUser?.DisplayName ?? m.SenderBot?.Name ?? "Unknown";

    private static string Cut(string text, int max) => text.Length > max ? text[..max] + "..." : text;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Word();
}
