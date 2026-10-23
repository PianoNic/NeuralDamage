using Microsoft.Extensions.Logging;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Images;
using NeuralDamage.Infrastructure.Services.Stickers;
using NeuralDamage.Infrastructure.Services.WebImages;

namespace NeuralDamage.Infrastructure.Services.Stories;

/// <summary>
/// A bot's own story: the model gets a short schema and the bot's sticker
/// list and writes the spec itself, which is then checked and rendered like
/// anyone's. A spec that does not parse or breaks the rules is dropped.
/// </summary>
public class StoryWriter(
    NeuralDamageDbContext db,
    IOpenRouterService openRouter,
    FeedPostWriter posts,
    StoryPublisher publisher,
    ILogger<StoryWriter> logger,
    BotClock? clock = null,
    ImageGenerator? images = null,
    WebImageStore? web = null)
{
    private readonly BotClock _clock = clock ?? BotClock.Default;

    /// <summary>The image web search is set up, so specs may ask for <c>search:</c> pictures.</summary>
    public bool CanSearch => web is { IsEnabled: true };

    /// <summary>What the schema says about pictures from the web, when the search is set up.</summary>
    public const string SearchNote =
        "Any \"image\" may also be \"search:<what to search the web for>\" to use a real photo from the web: " +
        "name the thing, place or person precisely, in English, the way you would type it into an image search.";

    /// <summary>The schema the model writes a story to, short enough to sit in every prompt.</summary>
    public static readonly string Schema = "Reply with one JSON object and nothing else:\n" + LayerSchema(StoryFormats.Story);

    /// <summary>
    /// The layer spec for a canvas of <paramref name="format"/>, the same for a
    /// story and a post's picture; a post's names its format.
    /// </summary>
    public static string LayerSchema(string format)
    {
        var formatKey = format == StoryFormats.Story ? "" : $"\"format\":\"{format}\",";
        var what = format == StoryFormats.Story ? "story" : "picture";
        return $$"""
            {{{formatKey}}"background":{"color":"#rrggbb","image":"wiki:<something to look up>"},"layers":[
              {"type":"text","text":"...","x":50,"y":30,"size":28,"color":"#ffffff","weight":700},
              {"type":"sticker","name":"<a sticker name from the list>","x":50,"y":70,"size":130,"rotation":-8},
              {"type":"image","image":"wiki:<something to look up>","x":50,"y":55,"size":220}]}
            The {{what}} is {{StoryFormats.Shape(format)}}. x and y are percent from the top left, the layer's centre. Text size 10 to 120 (28 is a headline), sticker and image size 30 to 360 (360 is the full width). At most 12 layers, 3 images and 200 characters per text. "image" is optional everywhere; "wiki:" looks up a picture by name on Wikipedia, so name a real place, thing or person. Use only stickers from the list. Pick colours that read well on each other.
            """;
    }

    /// <param name="offerImage">The bot may have its background drawn by the image generator, when it is set up.</param>
    public async Task<StoryDto?> WriteAsync(Bot bot, DateTimeOffset now, CancellationToken ct = default, bool offerImage = false)
    {
        var stickers = await StickerCatalog.LoadAsync(db, ct);
        offerImage = offerImage && images is { IsEnabled: true };
        var system = SystemPrompt(bot, stickers.ForPrompt, offerImage, CanSearch);
        List<ChatMessage> turns =
        [
            new(ChatMessage.Note, $"(It is {_clock.Describe(now)}. {BotPromptBuilder.Reminder(bot)} Make one story for today, in character: a thought, a mood, a show-off, a joke. Keep the text short, the way stories are.)"),
        ];

        var reply = await BotResponseOrchestrator.GenerateAsync(openRouter, bot.Name, posts.ModelFor(bot), Math.Min(bot.Temperature, 1.0), system, turns, logger, ct);
        var json = ExtractJson(reply);
        var (spec, error) = StorySpecRules.Parse(json);
        if (spec is null)
        {
            logger.LogInformation("Bot {BotName}'s story was dropped: {Error}", bot.Name, error);
            return null;
        }
        // Whatever the model wrote, a story is a story.
        spec = await WithPictureAsync(bot, spec with { Format = null }, offerImage, ct);

        var result = await publisher.PublishAsync(spec, null, bot.Id, ct);
        if (result.IsFailure)
        {
            logger.LogInformation("Bot {BotName}'s story was dropped: {Error}", bot.Name, result.Error);
            return null;
        }
        return result.Value;
    }

    /// <summary>What the schema says about a generated background, when the bot is offered one.</summary>
    public static string GenerateNote(bool canIncludeMe) =>
        "The background \"image\" may also be \"generate:<what the picture shows>\" to have it drawn by an image generator: " +
        "describe one photo or scene in English, in detail, and put words that should appear in it in quotes. " +
        (canIncludeMe ? "Use \"generate-me:<...>\" instead to be in it yourself, with your own face, and call yourself \"this person\" in it. " : "") +
        "Only the background can be drawn.";

    /// <summary>The prompt of a background the model asked to have drawn, and whether it wants to be in it.</summary>
    public static (string Prompt, bool IncludeMe)? GenerateAsk(string? image)
    {
        var value = image?.Trim() ?? "";
        if (value.StartsWith("generate-me:", StringComparison.OrdinalIgnoreCase))
            return value[12..].Trim() is { Length: > 0 } me ? (me, true) : null;
        if (value.StartsWith("generate:", StringComparison.OrdinalIgnoreCase))
            return value[9..].Trim() is { Length: > 0 } scene ? (scene, false) : null;
        return null;
    }

    /// <summary>The query of a picture the model asked to have found on the web.</summary>
    public static string? SearchAsk(string? image)
    {
        var value = image?.Trim() ?? "";
        return value.StartsWith("search:", StringComparison.OrdinalIgnoreCase) && value[7..].Trim() is { Length: > 0 } query ? query : null;
    }

    /// <summary>
    /// Fetches the pictures the bot asked to find on the web and draws the
    /// background it asked to have drawn, and points the spec at them. A
    /// background whose picture fails is a plain colour; an image layer whose
    /// picture fails is dropped. Image layers that ask for a drawing are
    /// dropped: only the background is drawn, and only when
    /// <paramref name="draw"/> says the bot was offered it.
    /// </summary>
    public async Task<StorySpec> WithPictureAsync(Bot bot, StorySpec spec, bool draw, CancellationToken ct = default)
    {
        // "generate:" and "generate-me:", never a picture already made ("generated:<id>").
        static bool Asks(string? image) =>
            image?.Trim() is { } value
            && (value.StartsWith("generate:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("generate-me:", StringComparison.OrdinalIgnoreCase));
        var stickers = await StickerCatalog.LoadAsync(db, ct);
        spec = StorySpecRules.WithoutUnknownStickers(spec, name => stickers.Find(name)?.Name);
        spec = spec with { Layers = spec.Layers.Where(l => !(l.Type == "image" && Asks(l.Image))).ToList() };
        spec = await WithSearchedAsync(bot, spec, ct);
        if (!Asks(spec.Background.Image))
            return spec;

        string? drawn = null;
        if (draw && GenerateAsk(spec.Background.Image) is { } ask && images is { IsEnabled: true })
        {
            var (width, height) = StoryFormats.Picture(StoryFormats.Of(spec));
            var started = await images.StartAsync(new ImageAsk(ask.Prompt, null, bot.Id, ask.IncludeMe ? bot.Id : null, width, height), ct);
            if (started.IsSuccess && await started.Value!.Done.WaitAsync(ct))
                drawn = $"generated:{started.Value.Image.Id}";
            else
                logger.LogInformation("Bot {BotName}'s {Format} goes without its background picture: {Error}", bot.Name, StoryFormats.Of(spec), started.Error ?? "it failed");
        }
        return spec with { Background = spec.Background with { Image = drawn, Fit = drawn is null ? null : spec.Background.Fit } };
    }

    /// <summary>The spec with its <c>search:</c> pictures found and stored, or left out when they cannot be.</summary>
    private async Task<StorySpec> WithSearchedAsync(Bot bot, StorySpec spec, CancellationToken ct)
    {
        var (width, height) = StoryFormats.Canvas(StoryFormats.Of(spec));
        var found = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        async Task<string?> FindAsync(string query, double aspect)
        {
            if (found.TryGetValue(query, out var known))
                return known;
            string? reference = null;
            if (web is { IsEnabled: true } && await web.FindForBotAsync(bot.Id, query, aspect, ct) is { } image)
                reference = $"generated:{image.Id}";
            else
                logger.LogInformation("Bot {BotName}'s picture search found nothing to use", bot.Name);
            return found[query] = reference;
        }

        var background = spec.Background;
        if (SearchAsk(background.Image) is { } backgroundQuery)
        {
            var reference = await FindAsync(backgroundQuery, (double)width / height);
            background = background with { Image = reference, Fit = reference is null ? null : "cover" };
        }

        var layers = new List<StoryLayer>();
        foreach (var layer in spec.Layers)
        {
            if (layer.Type == "image" && SearchAsk(layer.Image) is { } query)
            {
                if (await FindAsync(query, 1.0) is { } reference)
                    layers.Add(layer with { Image = reference });
                continue;
            }
            layers.Add(layer);
        }
        return spec with { Background = background, Layers = layers };
    }

    public static string SystemPrompt(Bot bot, IReadOnlyList<BotSticker> stickers, bool offerImage = false, bool canSearch = false)
    {
        var list = stickers.Count == 0 ? "(none)" : string.Join("\n", stickers.Select(s => $"{s.Name}: {s.Description}"));
        return $"""
            {BotPromptBuilder.Character(bot)}

            You are "{bot.Name}", about to post a story on a chat app: a picture everyone sees for 24 hours, made of a background and layers of text, stickers and pictures.

            {Schema}{(canSearch ? "\n" + SearchNote : "")}{(offerImage ? "\n" + GenerateNote(bot.AvatarUrl is not null) : "")}

            Stickers (name: what it shows):
            {list}

            Write the text in character, the way you would post it. No em dashes.
            """;
    }

    /// <summary>The JSON object in a reply, without the code fence or chatter some models add around it.</summary>
    public static string? ExtractJson(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return null;
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        return start >= 0 && end > start ? reply[start..(end + 1)] : null;
    }
}
