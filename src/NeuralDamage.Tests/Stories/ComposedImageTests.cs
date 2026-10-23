using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Images;
using NeuralDamage.Infrastructure.Services.Stories;
using NeuralDamage.Tests.Helpers;
using NeuralDamage.Tests.Images;
using NSubstitute;
using SkiaSharp;

namespace NeuralDamage.Tests.Stories;

/// <summary>Post pictures composed from layers, like stories, by people and by bots.</summary>
public class ComposedImageTests
{
    private static readonly FixedTime Afternoon = new(new DateTimeOffset(2026, 7, 15, 13, 0, 0, TimeSpan.Zero));
    private static readonly Func<string, bool> NoStickers = _ => false;

    private static byte[] Solid(SKColor color, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static StorySpec Spec(string? format, string color = "#123456") =>
        new(new StoryBackground(color), [new StoryLayer("text", 50, 50, 28, Text: "Kitchen closed", Color: "#ffffff", Weight: 700)], format);

    [Test]
    [Arguments(null, 1080, 1920)]
    [Arguments("story", 1080, 1920)]
    [Arguments("post-4x5", 1080, 1350)]
    [Arguments("post-1x1", 1080, 1080)]
    public async Task EachFormat_RendersAtItsOwnSize(string? format, int width, int height)
    {
        // A full-width white bar low down shows that positions are percent of this canvas.
        var spec = Spec(format) with { Layers = [new StoryLayer("image", 50, 95, 360, Image: "wiki:Bar")] };
        var bar = Solid(SKColors.White, 100, 4);

        var png = FeedHarness.SharedRenderer.Render(spec, StoryAssets.None with { Images = new Dictionary<string, byte[]> { ["wiki:Bar"] = bar } });

        using var bitmap = SKBitmap.Decode(png);
        await Assert.That((bitmap.Width, bitmap.Height)).IsEqualTo((width, height));
        await Assert.That(bitmap.GetPixel(width / 2, 5)).IsEqualTo(new SKColor(0x12, 0x34, 0x56));
        await Assert.That(bitmap.GetPixel(width / 2, (int)(height * 0.95))).IsEqualTo(SKColors.White);
    }

    [Test]
    public async Task TheFormat_IsChecked_AndEachUseTakesOnlyItsOwn()
    {
        await Assert.That(StorySpecRules.Validate(Spec("post-4x5"), NoStickers)).IsEmpty();
        await Assert.That(StorySpecRules.Validate(Spec("post-1x1"), NoStickers)).IsEmpty();
        await Assert.That(string.Join(" ", StorySpecRules.Validate(Spec("post-16x9"), NoStickers))).Contains("format");
        await Assert.That(StorySpecRules.Parse("""{"format":"post-1x1","background":{"color":"#000"},"layers":[]}""").Spec!.Format).IsEqualTo("post-1x1");

        using var h = await FeedHarness.CreateAsync(botCount: 1);
        using var scope = h.Provider.CreateScope();
        var story = await scope.ServiceProvider.GetRequiredService<StoryPublisher>().PublishAsync(Spec("post-4x5"), h.User.Id, null);
        var post = await scope.ServiceProvider.GetRequiredService<ImageComposer>().ComposeAsync(Spec(null), h.User.Id, null);

        await Assert.That(story.IsFailure).IsTrue();
        await Assert.That(post.IsFailure).IsTrue();
        await Assert.That(post.Error!).Contains("4:5");
    }

    [Test]
    public async Task APersonsComposedPicture_IsReadyAtOnce_GoesOnTheirPost_AndKeepsItsSpec()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1);
        using var scope = h.Provider.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<ImageComposer>();

        var composed = await new ComposeImageHandler(composer).Handle(new ComposeImageCommand(h.User.Id, Spec("post-4x5")), default);

        await Assert.That(composed.IsSuccess).IsTrue();
        var dto = composed.Value!;
        await Assert.That(dto.Status).IsEqualTo(GeneratedImageStatus.Ready);
        await Assert.That(dto.Kind).IsEqualTo(GeneratedImageKind.Composed);
        await Assert.That((dto.Width, dto.Height)).IsEqualTo((1080, 1350));
        await Assert.That(dto.Spec!.Layers.Single().Text).IsEqualTo("Kitchen closed");
        await Assert.That(dto.Prompt).IsEqualTo("Kitchen closed");
        using (var png = SKBitmap.Decode(h.Storage.Generated[dto.Id]))
            await Assert.That(png.Height).IsEqualTo(1350);

        var post = await new CreatePostHandler(h.Db, h.Notifier, h.Queue).Handle(new CreatePostCommand(h.User.Id, "closing time", ImageId: dto.Id), default);
        await Assert.That(post.IsSuccess).IsTrue();
        await Assert.That(post.Value!.Image!.Kind).IsEqualTo(GeneratedImageKind.Composed);
        var someoneElse = await new CreatePostHandler(h.Db, h.Notifier, h.Queue).Handle(new CreatePostCommand(Guid.NewGuid(), "mine now", ImageId: dto.Id), default);
        await Assert.That(someoneElse.IsFailure).IsTrue();
    }

    [Test]
    public async Task ComposedPictures_DoNotCountAgainstTheGeneratorsDailyCap()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1, renderer: new FakeRenderer(), imageGen: new ImageGenOptions { BaseUrl = "http://comfyui.example.com/", MaxPerDay = 1 });
        using var scope = h.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ImageComposer>().ComposeAsync(Spec("post-1x1"), h.User.Id, null);
        await scope.ServiceProvider.GetRequiredService<ImageComposer>().ComposeAsync(Spec("post-1x1"), h.User.Id, null);

        var drawn = await h.Images!.StartAsync(new ImageAsk("a cat", h.User.Id, null));

        await Assert.That(drawn.IsSuccess).IsTrue();
    }

    [Test]
    public async Task StructuredOutput_WithASpec_IsAPostFormat()
    {
        var (text, image) = FeedPostOutput.Parse("""
            {"text": "closing time", "image": {"spec": {"format": "post-1x1", "background": {"color": "#000000"}, "layers": [{"type": "text", "text": "CLOSED", "x": 50, "y": 50, "size": 40}]}}}
            """);
        var (_, story) = FeedPostOutput.Parse("""{"text": "x", "image": {"spec": {"background": {"color": "#000000"}, "layers": []}}}""");
        var (_, broken) = FeedPostOutput.Parse("""{"text": "x", "image": {"spec": {"layers": []}}}""");
        var (_, unwrapped) = FeedPostOutput.Parse("""{"text": "x", "image": {"format": "post-1x1", "background": {"color": "#000000"}, "layers": []}}""");
        await Assert.That(unwrapped!.Spec!.Format).IsEqualTo("post-1x1");

        await Assert.That(text).IsEqualTo("closing time");
        await Assert.That(image!.Prompt).IsNull();
        await Assert.That(image.Spec!.Format).IsEqualTo("post-1x1");
        await Assert.That(story!.Spec!.Format).IsEqualTo("post-4x5");
        await Assert.That(broken).IsNull();
    }

    [Test]
    public async Task ABotOfferedAPicture_PostsOneItComposed()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 1 }, time: Afternoon);
        h.Reply("""
            {"text": "kitchen closed, go home", "image": {"spec": {"format": "post-4x5", "background": {"color": "#123456"},
              "layers": [{"type": "text", "text": "CLOSED", "x": 50, "y": 40, "size": 48, "color": "#ffffff", "weight": 700}]}}}
            """);

        var post = await h.Scheduler.PostAsync(h.Bots[0], Afternoon.Now, 0.5, imageRoll: 0);

        await Assert.That(post!.Content).IsEqualTo("kitchen closed, go home");
        await Assert.That(post.ImageId).IsNotNull();
        var image = await h.Db.GeneratedImages.AsNoTracking().SingleAsync(i => i.Id == post.ImageId);
        await Assert.That(image.Kind).IsEqualTo(GeneratedImageKind.Composed);
        await Assert.That(image.BotId).IsEqualTo(h.Bots[0].Id);
        await Assert.That(image.Spec!).Contains("CLOSED");
        using var png = SKBitmap.Decode(h.Storage.Generated[image.Id]);
        await Assert.That((png.Width, png.Height)).IsEqualTo((1080, 1350));
        await Assert.That(png.GetPixel(5, 5)).IsEqualTo(new SKColor(0x12, 0x34, 0x56));
        await h.Notifier.Received(1).PostNew(Arg.Is<NeuralDamage.Infrastructure.Dtos.PostDto>(p => p.Image != null && p.Image.Prompt == "CLOSED"));
    }

    [Test]
    public async Task ABotsComposedPicture_GetsItsBackgroundDrawnInThePostsShape()
    {
        var drawer = new FakeRenderer();
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 1 }, time: Afternoon, renderer: drawer);
        h.Reply("""{"text": "view from work", "image": {"spec": {"background": {"color": "#000000", "image": "generate:a kitchen at night"}, "layers": [{"type": "text", "text": "overtime", "x": 50, "y": 20, "size": 30}]}}}""");

        var post = await h.Scheduler.PostAsync(h.Bots[0], Afternoon.Now, 0.5, imageRoll: 0);

        await Assert.That(post!.ImageId).IsNotNull();
        var request = drawer.Requests.Single();
        await Assert.That((request.Width, request.Height)).IsEqualTo((896, 1120));
        var composed = await h.Db.GeneratedImages.AsNoTracking().SingleAsync(i => i.Id == post.ImageId);
        await Assert.That(composed.Spec!).Contains("generated:");
    }

    [Test]
    public async Task AMadeUpSticker_IsLeftOut_AndThePictureStays()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 1 }, time: Afternoon);
        h.Reply("""{"text": "look", "image": {"format": "post-1x1", "background": {"color": "#000000"}, "layers": [{"type": "text", "text": "RAW", "x": 50, "y": 50, "size": 60}, {"type": "sticker", "name": "angry_face", "x": 50, "y": 80, "size": 100}]}}""");

        var post = await h.Scheduler.PostAsync(h.Bots[0], Afternoon.Now, 0.5, imageRoll: 0);

        var image = await h.Db.GeneratedImages.AsNoTracking().SingleAsync(i => i.Id == post!.ImageId);
        await Assert.That(image.Spec!).DoesNotContain("angry_face");
        await Assert.That(image.Height).IsEqualTo(1080);
    }

    [Test]
    public async Task ABrokenComposedPicture_LeavesThePostWithoutOne()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 1 }, time: Afternoon);
        h.Reply("""{"text": "look", "image": {"spec": {"format": "post-4x5", "background": {"color": "#000000"}, "layers": [{"type": "sticker", "name": "no-such-sticker", "x": 50, "y": 50, "size": 100}]}}}""");

        var post = await h.Scheduler.PostAsync(h.Bots[0], Afternoon.Now, 0.5, imageRoll: 0);

        await Assert.That(post!.Content).IsEqualTo("look");
        await Assert.That(post.ImageId).IsNull();
    }
}
