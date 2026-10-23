using Microsoft.Extensions.Logging;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Images;
using NeuralDamage.Infrastructure.Services.Stickers;

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
    ImageGenerator? images = null)
{
    /// <summary>A story's generated background: 9:16, like the story.</summary>
    public const int PictureWidth = 768, PictureHeight = 1344;

    private readonly BotClock _clock = clock ?? BotClock.Default;

    /// <summary>The schema the model writes to, short enough to sit in every prompt.</summary>
    public const string Schema = """
        Reply with one JSON object and nothing else:
        {"background":{"color":"#rrggbb","image":"wiki:<something to look up>"},"layers":[
          {"type":"text","text":"...","x":50,"y":30,"size":28,"color":"#ffffff","weight":700},
          {"type":"sticker","name":"<a sticker name from the list>","x":50,"y":70,"size":130,"rotation":-8},
          {"type":"image","image":"wiki:<something to look up>","x":50,"y":55,"size":220}]}
        The story is 9:16. x and y are percent from the top left, the layer's centre. Text size 10 to 120 (28 is a headline), sticker and image size 30 to 360 (360 is the full width). At most 12 layers, 3 images and 200 characters per text. "image" is optional everywhere; "wiki:" looks up a picture by name on Wikipedia, so name a real place, thing or person. Use only stickers from the list. Pick colours that read well on each other.
        """;

    /// <param name="offerImage">The bot may have its background drawn by the image generator, when it is set up.</param>
    public async Task<StoryDto?> WriteAsync(Bot bot, DateTimeOffset now, CancellationToken ct = default, bool offerImage = false)
    {
        var stickers = await StickerCatalog.LoadAsync(db, ct);
        offerImage = offerImage && images is { IsEnabled: true };
        var system = SystemPrompt(bot, stickers.ForPrompt, offerImage);
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
        spec = await WithPictureAsync(bot, spec, offerImage, ct);

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

    /// <summary>
    /// Draws the background the bot asked for and points the spec at it; the
    /// background is a plain colour when the picture fails. Image layers that
    /// ask for a drawing are dropped: only the background is drawn, and only
    /// when <paramref name="draw"/> says the bot was offered it.
    /// </summary>
    public async Task<StorySpec> WithPictureAsync(Bot bot, StorySpec spec, bool draw, CancellationToken ct = default)
    {
        static bool Asks(string? image) => image?.Trim().StartsWith("generate", StringComparison.OrdinalIgnoreCase) == true;
        spec = spec with { Layers = spec.Layers.Where(l => !(l.Type == "image" && Asks(l.Image))).ToList() };
        if (!Asks(spec.Background.Image))
            return spec;

        string? drawn = null;
        if (draw && GenerateAsk(spec.Background.Image) is { } ask && images is { IsEnabled: true })
        {
            var started = await images.StartAsync(new ImageAsk(ask.Prompt, null, bot.Id, ask.IncludeMe ? bot.Id : null, PictureWidth, PictureHeight), ct);
            if (started.IsSuccess && await started.Value!.Done.WaitAsync(ct))
                drawn = $"generated:{started.Value.Image.Id}";
            else
                logger.LogInformation("Bot {BotName}'s story goes without its picture: {Error}", bot.Name, started.Error ?? "it failed");
        }
        return spec with { Background = spec.Background with { Image = drawn, Fit = drawn is null ? null : spec.Background.Fit } };
    }

    public static string SystemPrompt(Bot bot, IReadOnlyList<BotSticker> stickers, bool offerImage = false)
    {
        var list = stickers.Count == 0 ? "(none)" : string.Join("\n", stickers.Select(s => $"{s.Name}: {s.Description}"));
        return $"""
            {BotPromptBuilder.Character(bot)}

            You are "{bot.Name}", about to post a story on a chat app: a picture everyone sees for 24 hours, made of a background and layers of text, stickers and pictures.

            {Schema}{(offerImage ? "\n" + GenerateNote(bot.AvatarUrl is not null) : "")}

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
