using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotPictures;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Images;
using NeuralDamage.Infrastructure.Services.Stories;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using SkiaSharp;

namespace NeuralDamage.Tests.Images;

/// <summary>Generated pictures as story backgrounds: a person's from the editor, a bot's on its own.</summary>
public class StoryImageTests
{
    private static readonly StoryRenderer Renderer = new();

    private static byte[] Picture(SKColor color, int width = 768, int height = 1344)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<(FeedHarness H, FakeRenderer Drawer, StoryPublisher Publisher)> CreateAsync(Func<RenderRequest, CancellationToken, Task<byte[]>>? draw = null)
    {
        var drawer = new FakeRenderer { Draw = draw ?? ((_, _) => Task.FromResult(Picture(SKColors.Green))) };
        var h = await FeedHarness.CreateAsync(botCount: 1, renderer: drawer);
        var publisher = new StoryPublisher(h.Db, h.Storage, Renderer, Substitute.For<IBotPictureFinder>(), h.Notifier, NullLogger<StoryPublisher>.Instance);
        return (h, drawer, publisher);
    }

    private static StoryWriter Writer(FeedHarness h, StoryPublisher publisher) =>
        new(h.Db, h.OpenRouter, new FeedPostWriter(h.Db, h.OpenRouter, new ConfigurationBuilder().Build(), NullLogger<FeedPostWriter>.Instance),
            publisher, NullLogger<StoryWriter>.Instance, images: h.Images);

    private static StorySpec Spec(string? background) =>
        new(new StoryBackground("#1d3347", background, background is null ? null : "cover"), [new StoryLayer("text", 50, 30, 28, Text: "hi", Color: "#ffffff", Weight: 700)]);

    [Test]
    public async Task APersonsOwnGeneratedPicture_CanBeTheBackground_NobodyElses()
    {
        var (h, _, publisher) = await CreateAsync();
        using var _h = h;
        var ticket = (await h.Images!.StartAsync(new ImageAsk("a green field", h.User.Id, null, Width: 768, Height: 1344))).Value!;
        await ticket.Done;

        var mine = await publisher.RenderAsync(Spec($"generated:{ticket.Image.Id}"), h.User.Id);
        var theirs = await publisher.RenderAsync(Spec($"generated:{ticket.Image.Id}"), Guid.NewGuid());

        await Assert.That(mine.IsSuccess).IsTrue();
        using (var bitmap = SKBitmap.Decode(mine.Value))
            await Assert.That(bitmap.GetPixel(5, 5)).IsEqualTo(SKColors.Green);
        await Assert.That(theirs.IsFailure).IsTrue();
    }

    [Test]
    public async Task ABotOfferedAPicture_GetsItsBackgroundDrawn_WithItsFace()
    {
        var (h, drawer, publisher) = await CreateAsync();
        using var _h = h;
        var bot = h.Bots[0];
        bot.AvatarUrl = "/api/bots/x/avatar";
        await h.Db.SaveChangesAsync();
        h.Storage.BotAvatars[bot.Id] = Picture(SKColors.Orange, 64, 64);
        string? system = null;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            system = ci.ArgAt<string>(2);
            return """{"background":{"color":"#000000","image":"generate-me:this person on a beach at sunset"},"layers":[{"type":"text","text":"off duty","x":50,"y":20,"size":30,"color":"#ffffff"},{"type":"image","image":"generate:a crab","x":50,"y":70,"size":100}]}""";
        });

        var story = await Writer(h, publisher).WriteAsync(bot, DateTimeOffset.UtcNow, offerImage: true);

        await Assert.That(system!).Contains("generate-me:");
        await Assert.That(story).IsNotNull();
        var request = drawer.Requests.Single();
        await Assert.That(request.Workflow).IsEqualTo(ImageWorkflows.Self);
        await Assert.That((request.Width, request.Height)).IsEqualTo((768, 1344));
        var saved = await h.Db.Stories.AsNoTracking().SingleAsync();
        var image = await h.Db.GeneratedImages.AsNoTracking().SingleAsync();
        await Assert.That(saved.Spec).Contains($"generated:{image.Id}");
        await Assert.That(saved.Spec).DoesNotContain("a crab");
        using var png = SKBitmap.Decode(h.Storage.StoryFiles[story!.Id]);
        await Assert.That(png.GetPixel(5, 5)).IsEqualTo(SKColors.Green);
    }

    [Test]
    public async Task WhenThePictureFails_TheStoryGoesOutOnItsColour()
    {
        var (h, _, publisher) = await CreateAsync((_, _) => throw new ImageGenerationException("The image generator cannot be reached."));
        using var _h = h;
        h.Reply("""{"background":{"color":"#123456","image":"generate:a sunset"},"layers":[{"type":"text","text":"mood","x":50,"y":50,"size":30,"color":"#ffffff"}]}""");

        var story = await Writer(h, publisher).WriteAsync(h.Bots[0], DateTimeOffset.UtcNow, offerImage: true);

        await Assert.That(story).IsNotNull();
        await Assert.That((await h.Db.Stories.AsNoTracking().SingleAsync()).Spec).DoesNotContain("generate");
    }

    [Test]
    public async Task NotOffered_ThePromptSaysNothingOfIt()
    {
        var (h, drawer, publisher) = await CreateAsync();
        using var _h = h;
        string? system = null;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            system = ci.ArgAt<string>(2);
            return """{"background":{"color":"#123456","image":"generate:a sunset"},"layers":[{"type":"text","text":"mood","x":50,"y":50,"size":30,"color":"#ffffff"}]}""";
        });

        var story = await Writer(h, publisher).WriteAsync(h.Bots[0], DateTimeOffset.UtcNow);

        await Assert.That(system!).DoesNotContain("generate:");
        await Assert.That(story).IsNotNull();
        await Assert.That(drawer.Requests).IsEmpty();
    }

    [Test]
    public async Task GenerateAsks_AreRead()
    {
        await Assert.That(StoryWriter.GenerateAsk("generate:a cat")).IsEqualTo(("a cat", false));
        await Assert.That(StoryWriter.GenerateAsk("generate-me: this person skiing")).IsEqualTo(("this person skiing", true));
        await Assert.That(StoryWriter.GenerateAsk("generate:")).IsNull();
        await Assert.That(StoryWriter.GenerateAsk("wiki:Paris")).IsNull();
        await Assert.That(StoryImageRef.Parse($"generated:{Guid.Empty}")).IsEqualTo(new StoryImageRef.Generated(Guid.Empty));
    }
}
