using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Images;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using SkiaSharp;

namespace NeuralDamage.Tests.Images;

/// <summary>A renderer that draws a placeholder, or fails, and remembers what it was asked.</summary>
public sealed class FakeRenderer : IImageRenderer
{
    public ConcurrentQueue<RenderRequest> Requests { get; } = new();
    public Func<RenderRequest, CancellationToken, Task<byte[]>> Draw { get; set; } = (_, _) => Task.FromResult(TestImages.Png(1024, 1024));

    public Task<byte[]> RenderAsync(RenderRequest request, CancellationToken ct = default)
    {
        Requests.Enqueue(request);
        return Draw(request, ct);
    }
}

/// <summary>The picture maker: the cap, the face fallback, failures, and people's pictures on posts.</summary>
public class ImageGeneratorTests
{
    private static byte[] Avatar()
    {
        using var bitmap = new SKBitmap(64, 64);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.Teal);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Test]
    public async Task APicture_IsDrawnAndStored()
    {
        var renderer = new FakeRenderer();
        using var h = await FeedHarness.CreateAsync(renderer: renderer);

        var ticket = (await h.Images!.StartAsync(new ImageAsk("a red apple", h.User.Id, null))).Value!;

        await Assert.That(await ticket.Done).IsTrue();
        var image = await h.Db.GeneratedImages.AsNoTracking().SingleAsync();
        await Assert.That(image.Status).IsEqualTo(GeneratedImageStatus.Ready);
        await Assert.That(image.Width).IsEqualTo(1024);
        await Assert.That(h.Storage.Generated.ContainsKey(image.Id)).IsTrue();
        await Assert.That(renderer.Requests.Single().Workflow).IsEqualTo(ImageWorkflows.Scene);
    }

    [Test]
    public async Task WithAFaceThatHasAPicture_TheSelfWorkflowGetsThePaddedReference()
    {
        var renderer = new FakeRenderer();
        using var h = await FeedHarness.CreateAsync(renderer: renderer);
        h.Storage.BotAvatars[h.Bots[0].Id] = Avatar();

        var ticket = (await h.Images!.StartAsync(new ImageAsk("this person cooking", h.User.Id, null, h.Bots[0].Id))).Value!;
        await ticket.Done;

        var request = renderer.Requests.Single();
        await Assert.That(request.Workflow).IsEqualTo(ImageWorkflows.Self);
        await Assert.That(request.Prompt).StartsWith("this person cooking. The person from the reference image");
        using var reference = SKBitmap.Decode(request.References!.Single());
        await Assert.That(reference.Width).IsEqualTo(ReferenceImage.Size);
        await Assert.That((await h.Db.GeneratedImages.AsNoTracking().SingleAsync()).FaceBotId).IsEqualTo(h.Bots[0].Id);
    }

    [Test]
    public async Task WithReferences_TheSelfWorkflowGetsTheAvatarAndThemUpToThree_AndTheLooks()
    {
        var renderer = new FakeRenderer();
        using var h = await FeedHarness.CreateAsync(renderer: renderer);
        var bot = h.Bots[0];
        h.Storage.BotAvatars[bot.Id] = Avatar();
        bot.Looks = "Red hair in a messy bun, round glasses.";
        foreach (var hair in new[] { SKColors.Red, SKColors.Gold, SKColors.Black })
        {
            var reference = new BotReference { BotId = bot.Id, ContentType = "image/png", SizeBytes = 1 };
            h.Db.BotReferences.Add(reference);
            h.Storage.BotReferences[(bot.Id, reference.Id)] = TestImages.Face(hair, 400, 300);
        }
        await h.Db.SaveChangesAsync();

        var ticket = (await h.Images!.StartAsync(new ImageAsk("me at the beach", null, bot.Id, bot.Id))).Value!;
        await Assert.That(await ticket.Done).IsTrue();

        var request = renderer.Requests.Single();
        await Assert.That(request.Workflow).IsEqualTo(ImageWorkflows.Self);
        await Assert.That(request.References!.Count).IsEqualTo(3);
        using var avatar = SKBitmap.Decode(request.References[0]);
        using var collage = SKBitmap.Decode(request.References[1]);
        await Assert.That(avatar.Width).IsEqualTo(ReferenceImage.Size);
        await Assert.That(collage.Width).IsEqualTo(ReferenceImage.LargeSize);
        await Assert.That(request.Prompt).Contains("reference images (all of the same person)");
        await Assert.That(request.Prompt).EndsWith("How they look: Red hair in a messy bun, round glasses.");
        // The stored prompt stays what was asked for.
        await Assert.That((await h.Db.GeneratedImages.AsNoTracking().SingleAsync()).Prompt).IsEqualTo("me at the beach");
    }

    [Test]
    public async Task WithOnlyReferences_TheFaceStillComesFromThem()
    {
        var renderer = new FakeRenderer();
        using var h = await FeedHarness.CreateAsync(renderer: renderer);
        var bot = h.Bots[0];
        var reference = new BotReference { BotId = bot.Id, ContentType = "image/png", SizeBytes = 1 };
        h.Db.BotReferences.Add(reference);
        h.Storage.BotReferences[(bot.Id, reference.Id)] = TestImages.Face(SKColors.Gold);
        await h.Db.SaveChangesAsync();

        var ticket = (await h.Images!.StartAsync(new ImageAsk("me cooking", null, bot.Id, bot.Id))).Value!;
        await ticket.Done;

        var request = renderer.Requests.Single();
        await Assert.That(request.Workflow).IsEqualTo(ImageWorkflows.Self);
        await Assert.That(request.References!.Count).IsEqualTo(1);
        await Assert.That(request.Prompt).IsEqualTo("me cooking. The person from the reference image is in this picture: keep their face and identity exactly the same.");
    }

    [Test]
    public async Task WithAFaceButNoPicture_ItFallsBackToAScene()
    {
        var renderer = new FakeRenderer();
        using var h = await FeedHarness.CreateAsync(renderer: renderer);

        var ticket = (await h.Images!.StartAsync(new ImageAsk("this person cooking", null, h.Bots[0].Id, h.Bots[0].Id))).Value!;
        await Assert.That(await ticket.Done).IsTrue();

        await Assert.That(renderer.Requests.Single().Workflow).IsEqualTo(ImageWorkflows.Scene);
        await Assert.That((await h.Db.GeneratedImages.AsNoTracking().SingleAsync()).FaceBotId).IsNull();
    }

    [Test]
    public async Task AFailingGenerator_MarksThePictureFailed()
    {
        var renderer = new FakeRenderer { Draw = (_, _) => throw new ImageGenerationException("The image generator cannot be reached.") };
        using var h = await FeedHarness.CreateAsync(renderer: renderer);

        var ticket = (await h.Images!.StartAsync(new ImageAsk("a red apple", h.User.Id, null))).Value!;

        await Assert.That(await ticket.Done).IsFalse();
        var image = await h.Db.GeneratedImages.AsNoTracking().SingleAsync();
        await Assert.That(image.Status).IsEqualTo(GeneratedImageStatus.Failed);
        await Assert.That(image.Error).IsEqualTo("The image generator cannot be reached.");
    }

    [Test]
    public async Task ThePicturesOfADay_AreCapped()
    {
        using var h = await FeedHarness.CreateAsync(renderer: new FakeRenderer(),
            imageGen: new ImageGenOptions { BaseUrl = "http://comfyui.example.com/", MaxPerDay = 2 });

        for (var i = 0; i < 2; i++)
            await (await h.Images!.StartAsync(new ImageAsk($"picture {i}", null, h.Bots[0].Id))).Value!.Done;
        var third = await h.Images!.StartAsync(new ImageAsk("one too many", null, h.Bots[0].Id));

        await Assert.That(third.IsFailure).IsTrue();
        await Assert.That(third.Error).Contains("today");
        await Assert.That(await h.Db.GeneratedImages.CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task APerson_WaitsForTheirLastPictureBeforeTheNext()
    {
        var release = new TaskCompletionSource<byte[]>();
        using var h = await FeedHarness.CreateAsync(renderer: new FakeRenderer { Draw = (_, _) => release.Task });

        var first = await h.Images!.StartAsync(new ImageAsk("one", h.User.Id, null));
        var second = await h.Images!.StartAsync(new ImageAsk("two", h.User.Id, null));

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(second.IsFailure).IsTrue();
        release.SetResult(TestImages.Png());
        await first.Value!.Done;
    }

    [Test]
    public async Task WithoutABaseUrl_NothingIsDrawn()
    {
        var renderer = new FakeRenderer();
        using var h = await FeedHarness.CreateAsync(renderer: renderer, imageGen: new ImageGenOptions());

        var result = await h.Images!.StartAsync(new ImageAsk("a red apple", h.User.Id, null));

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(renderer.Requests).IsEmpty();
    }

    [Test]
    public async Task APersonsReadyPicture_GoesOnTheirPost_AndOnlyTheirs()
    {
        using var h = await FeedHarness.CreateAsync(renderer: new FakeRenderer());
        var ticket = (await h.Images!.StartAsync(new ImageAsk("a red apple", h.User.Id, null))).Value!;
        await ticket.Done;
        var other = new User { ExternalId = "ext-2", Email = "bob@example.com", DisplayName = "Bob" };
        h.Db.Users.Add(other);
        await h.Db.SaveChangesAsync();
        var handler = new CreatePostHandler(h.Db, h.Notifier, h.Queue);

        var stolen = await handler.Handle(new CreatePostCommand(other.Id, "mine now", ImageId: ticket.Image.Id), default);
        var posted = await handler.Handle(new CreatePostCommand(h.User.Id, "", ImageId: ticket.Image.Id), default);
        var again = await handler.Handle(new CreatePostCommand(h.User.Id, "twice", ImageId: ticket.Image.Id), default);

        await Assert.That(stolen.IsFailure).IsTrue();
        await Assert.That(posted.IsSuccess).IsTrue();
        await Assert.That(posted.Value!.Image!.Url).IsEqualTo($"/api/images/{ticket.Image.Id}/file");
        await Assert.That(posted.Value.Image.Prompt).IsEqualTo("a red apple");
        await Assert.That(again.IsFailure).IsTrue();
    }

    [Test]
    public async Task DeletingAPost_DeletesItsPicture()
    {
        using var h = await FeedHarness.CreateAsync(renderer: new FakeRenderer());
        var ticket = (await h.Images!.StartAsync(new ImageAsk("a red apple", h.User.Id, null))).Value!;
        await ticket.Done;
        var post = (await new CreatePostHandler(h.Db, h.Notifier, h.Queue).Handle(new CreatePostCommand(h.User.Id, "look", ImageId: ticket.Image.Id), default)).Value!;

        await new DeletePostHandler(h.Db, AppAdmins.None, h.Notifier, h.Storage).Handle(new DeletePostCommand(h.User.Id, post.Id), default);

        await Assert.That(await h.Db.GeneratedImages.CountAsync()).IsEqualTo(0);
        await Assert.That(h.Storage.Generated).IsEmpty();
    }

    [Test]
    public async Task APictureNotPosted_CanBeDiscarded_ByItsMakerOnly()
    {
        using var h = await FeedHarness.CreateAsync(renderer: new FakeRenderer());
        var ticket = (await h.Images!.StartAsync(new ImageAsk("a red apple", h.User.Id, null))).Value!;
        await ticket.Done;
        var handler = new DiscardImageHandler(h.Db, h.Storage);

        await Assert.That((await handler.Handle(new DiscardImageCommand(Guid.NewGuid(), ticket.Image.Id), default)).IsFailure).IsTrue();
        await Assert.That((await handler.Handle(new DiscardImageCommand(h.User.Id, ticket.Image.Id), default)).IsSuccess).IsTrue();
        await Assert.That(h.Storage.Generated).IsEmpty();
    }
}

/// <summary>A bot's post with a picture: what the model answers, and what happens when the picture fails.</summary>
public class FeedImageTests
{
    private static readonly FixedTime Afternoon = new(new DateTimeOffset(2026, 7, 15, 13, 0, 0, TimeSpan.Zero));

    [Test]
    public async Task StructuredOutput_WithAPicture()
    {
        var (text, image) = FeedPostOutput.Parse("""{"text": "Dinner is served", "image": {"prompt": "a burnt lasagna", "includeMe": true}}""");
        await Assert.That(text).IsEqualTo("Dinner is served");
        await Assert.That(image).IsEqualTo(new PostImageAsk("a burnt lasagna", true));
    }

    [Test]
    public async Task StructuredOutput_WithoutAPicture_InCodeFences()
    {
        var (text, image) = FeedPostOutput.Parse("```json\n{\"text\": \"Just text today\", \"image\": null}\n```");
        await Assert.That(text).IsEqualTo("Just text today");
        await Assert.That(image).IsNull();
    }

    [Test]
    public async Task PlainProse_IsThePostItself_AndBrokenJson_IsNeverPosted()
    {
        await Assert.That(FeedPostOutput.Parse("no json here, just vibes")).IsEqualTo(("no json here, just vibes", (PostImageAsk?)null));
        await Assert.That(FeedPostOutput.Parse("{\"text\": \"oops").Text).IsEqualTo("");
        await Assert.That(FeedPostOutput.Parse("""{"text": "x", "image": {"prompt": "  "}}""").Image).IsNull();
    }

    [Test]
    public async Task ABotOfferedAPicture_PostsWithIt_AndItsOwnFaceWhenItHasOne()
    {
        var renderer = new FakeRenderer();
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 1 }, time: Afternoon, renderer: renderer);
        h.Bots[0].AvatarUrl = "/api/bots/x/avatar";
        await h.Db.SaveChangesAsync();
        h.Storage.BotAvatars[h.Bots[0].Id] = TestPng();
        List<ChatMessage>? turns = null;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            turns = ci.ArgAt<List<ChatMessage>>(3);
            return """{"text": "kitchen nightmares, live", "image": {"prompt": "this person yelling in a kitchen", "includeMe": true}}""";
        });

        var post = await h.Scheduler.PostAsync(h.Bots[0], Afternoon.Now, 0.5, imageRoll: 0);

        await Assert.That(turns![^1].Content).Contains("includeMe");
        await Assert.That(post!.Content).IsEqualTo("kitchen nightmares, live");
        await Assert.That(post.ImageId).IsNotNull();
        await Assert.That(renderer.Requests.Single().Workflow).IsEqualTo(ImageWorkflows.Self);
        await h.Notifier.Received(1).PostNew(Arg.Is<PostDto>(p => p.Image != null && p.Image.Prompt == "this person yelling in a kitchen"));
    }

    [Test]
    public async Task WhenThePictureFails_ThePostGoesOutWithoutIt()
    {
        var renderer = new FakeRenderer { Draw = (_, _) => throw new TimeoutException("too slow") };
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 1 }, time: Afternoon, renderer: renderer);
        h.Reply("""{"text": "look at this", "image": {"prompt": "a sunset", "includeMe": false}}""");

        var post = await h.Scheduler.PostAsync(h.Bots[0], Afternoon.Now, 0.5, imageRoll: 0);

        await Assert.That(post!.Content).IsEqualTo("look at this");
        await Assert.That(post.ImageId).IsNull();
    }

    [Test]
    public async Task WithTheGeneratorOff_OnlyAComposedPictureIsOffered_AndPlainWordsStayAPost()
    {
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 1 }, time: Afternoon);
        List<ChatMessage>? turns = null;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            turns = ci.ArgAt<List<ChatMessage>>(3);
            return "just words";
        });

        var post = await h.Scheduler.PostAsync(h.Bots[0], Afternoon.Now, 0.5, imageRoll: 0);

        await Assert.That(turns![^1].Content).Contains("\"spec\"");
        await Assert.That(turns[^1].Content).DoesNotContain("includeMe");
        await Assert.That(turns[^1].Content).DoesNotContain("generate:");
        await Assert.That(post!.Content).IsEqualTo("just words");
        await Assert.That(post.ImageId).IsNull();
    }

    [Test]
    public async Task AboveTheShare_NoPictureIsOffered()
    {
        var renderer = new FakeRenderer();
        using var h = await FeedHarness.CreateAsync(botCount: 1, feed: new FeedOptions { ChatInspiredShare = 0, ImageShare = 0.3 }, time: Afternoon, renderer: renderer);
        List<ChatMessage>? turns = null;
        h.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            turns = ci.ArgAt<List<ChatMessage>>(3);
            return "words only";
        });

        await h.Scheduler.PostAsync(h.Bots[0], Afternoon.Now, 0.5, imageRoll: 0.5);

        await Assert.That(turns![^1].Content).DoesNotContain("JSON");
        await Assert.That(renderer.Requests).IsEmpty();
    }

    private static byte[] TestPng()
    {
        using var bitmap = new SKBitmap(32, 32);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.Orange);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
