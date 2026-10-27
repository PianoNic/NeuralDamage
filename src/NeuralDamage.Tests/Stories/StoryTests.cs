using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.BackgroundServices;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Mappers;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotPictures;
using NeuralDamage.Infrastructure.Services.Feed;
using NeuralDamage.Infrastructure.Services.Stories;
using NeuralDamage.Tests.Helpers;
using NSubstitute;
using SkiaSharp;

namespace NeuralDamage.Tests.Stories;

/// <summary>What a spec may hold.</summary>
public class StorySpecTests
{
    private static readonly Func<string, bool> Stickers = name => name == "swole-doge";

    private static StorySpec Spec(params StoryLayer[] layers) => new(new StoryBackground("#1f3a2b"), [.. layers]);

    [Test]
    public async Task TheEditorsSpec_ParsesAndPasses()
    {
        var (spec, error) = StorySpecRules.Parse("""
            {"background":{"color":"#1f3a2b","image":"upload:6f8a1d9e-1111-4c4c-9a9a-0123456789ab","fit":"cover"},
             "layers":[{"type":"text","text":"Today I inspected the new kimchi factory.","x":50,"y":32,"size":28,"color":"#fafafa","weight":700},
                       {"type":"sticker","name":"swole-doge","x":50,"y":70,"size":130,"rotation":-8},
                       {"type":"image","image":"wiki:Eiffel Tower#1","x":50,"y":50,"size":200}]}
            """);
        await Assert.That(error).IsNull();
        await Assert.That(StorySpecRules.Validate(spec!, Stickers)).IsEmpty();
        await Assert.That(StoryImageRef.Parse("wiki:Eiffel Tower#1")).IsEqualTo(new StoryImageRef.Wiki("Eiffel Tower", 1));
    }

    [Test]
    public async Task UnknownStickers_BadColours_AndOutOfBoundSizes_AreRefused()
    {
        var errors = StorySpecRules.Validate(new StorySpec(new StoryBackground("green"),
        [
            new StoryLayer("sticker", 50, 50, 100, Name: "made-up"),
            new StoryLayer("text", 50, 50, 500, Text: "too big", Color: "#zzzzzz"),
            new StoryLayer("text", 150, 50, 20, Text: "off the story"),
            new StoryLayer("image", 50, 50, 100, Image: "https://example.com/x.png"),
            new StoryLayer("video", 50, 50, 100),
        ]), Stickers);

        await Assert.That(errors.Count).IsGreaterThanOrEqualTo(6);
        await Assert.That(string.Join(" ", errors)).Contains("no sticker called \"made-up\"");
    }

    [Test]
    public async Task TooMuchText_OrTooManyLayers_IsRefused()
    {
        var longText = StorySpecRules.Validate(Spec(new StoryLayer("text", Text: new string('a', StorySpecRules.MaxTextLength + 1))), Stickers);
        var many = StorySpecRules.Validate(Spec([.. Enumerable.Range(0, StorySpecRules.MaxLayers + 1).Select(_ => new StoryLayer("text", Text: "hi"))]), Stickers);
        var empty = StorySpecRules.Validate(Spec(), Stickers);

        await Assert.That(longText).IsNotEmpty();
        await Assert.That(many).IsNotEmpty();
        await Assert.That(empty).IsNotEmpty();
    }

    [Test]
    public async Task NotJson_IsNotASpec()
    {
        await Assert.That(StorySpecRules.Parse("a story about cats").Spec).IsNull();
        await Assert.That(StoryWriter.ExtractJson("Sure! ```json\n{\"background\":{\"color\":\"#000\"},\"layers\":[]}\n```")).IsEqualTo("{\"background\":{\"color\":\"#000\"},\"layers\":[]}");
    }
}

/// <summary>Posting, seeing, replying to and sweeping away stories.</summary>
public class StoryFlowTests
{
    private sealed class World : IDisposable
    {
        public required FeedHarness H { get; init; }
        public required InMemoryAttachmentStorage Storage { get; init; }
        public required IBotPictureFinder Pictures { get; init; }
        public required StoryPublisher Publisher { get; init; }
        public required ISender Sender { get; init; }
        public required IChatNotificationService ChatNotifications { get; init; }

        public void Dispose() => H.Dispose();
    }

    private static readonly StoryRenderer Renderer = new();

    private static async Task<World> CreateAsync()
    {
        var h = await FeedHarness.CreateAsync(botCount: 2);
        var storage = new InMemoryAttachmentStorage();
        var sticker = new Sticker { Name = "red-square", Description = "a red square", ContentType = "image/png", SizeBytes = 10, UploaderUserId = h.User.Id };
        h.Db.Stickers.Add(sticker);
        await h.Db.SaveChangesAsync();
        await storage.SaveStickerAsync(sticker.Id, StoryRendererTests.Square(SKColors.Red));

        var pictures = Substitute.For<IBotPictureFinder>();
        var candidate = new BotPictureCandidate("Tower", "https://example.com/tower", new Uri("https://example.com/tower.jpg"));
        pictures.FindAsync("Tower", Arg.Any<CancellationToken>()).Returns([candidate]);
        pictures.DownloadAsync(candidate, Arg.Any<CancellationToken>()).Returns(new BotPictureImage(StoryRendererTests.Square(SKColors.Blue, 80, 40), "image/png"));

        var sender = Substitute.For<ISender>();
        var chatNotifications = Substitute.For<IChatNotificationService>();
        var directChats = new OpenDirectChatHandler(h.Db, chatNotifications);
        sender.Send(Arg.Any<OpenDirectChatCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => directChats.Handle(call.Arg<OpenDirectChatCommand>(), default));
        return new World
        {
            H = h,
            Storage = storage,
            Pictures = pictures,
            Publisher = new StoryPublisher(h.Db, storage, Renderer, pictures, h.Notifier, NullLogger<StoryPublisher>.Instance),
            Sender = sender,
            ChatNotifications = chatNotifications,
        };
    }

    private static StorySpec Spec(string text = "Kitchen closed") =>
        new(new StoryBackground("#1d3347"), [new StoryLayer("text", 50, 30, 28, Text: text, Color: "#ffffff", Weight: 700), new StoryLayer("sticker", 50, 70, 100, Name: "red-square")]);

    [Test]
    public async Task APersonsStory_IsRenderedStoredAndAnnounced()
    {
        using var w = await CreateAsync();

        var result = await new CreateStoryHandler(w.Publisher, TestMusic.Library(w.H.Db)).Handle(new CreateStoryCommand(w.H.User.Id, Spec()), default);

        await Assert.That(result.IsSuccess).IsTrue();
        var png = w.Storage.StoryFiles[result.Value!.Id];
        using var bitmap = SKBitmap.Decode(png);
        await Assert.That(bitmap.Width).IsEqualTo(1080);
        await Assert.That(result.Value.Description).IsEqualTo("Kitchen closed / [sticker red-square]");
        await w.H.Notifier.Received(1).StoryNew(Arg.Is<StoryDto>(s => s.Id == result.Value.Id));
    }

    [Test]
    public async Task OnlyYourOwnUploads_AndPicturesTheLookupFinds_CanBeUsed()
    {
        using var w = await CreateAsync();
        var mine = new StoryImage { UploaderUserId = w.H.User.Id, ContentType = "image/png", SizeBytes = 10 };
        var theirs = new StoryImage { UploaderUserId = Guid.NewGuid(), ContentType = "image/png", SizeBytes = 10 };
        w.H.Db.StoryImages.AddRange(mine, theirs);
        await w.H.Db.SaveChangesAsync();
        await w.Storage.SaveStoryFileAsync(mine.Id, StoryRendererTests.Square(SKColors.Green));
        await w.Storage.SaveStoryFileAsync(theirs.Id, StoryRendererTests.Square(SKColors.Green));

        var withMine = await w.Publisher.RenderAsync(Spec() with { Background = new StoryBackground("#000000", $"upload:{mine.Id}", "cover") }, w.H.User.Id);
        var withTheirs = await w.Publisher.RenderAsync(Spec() with { Background = new StoryBackground("#000000", $"upload:{theirs.Id}", "cover") }, w.H.User.Id);
        var withWiki = await w.Publisher.RenderAsync(Spec() with { Layers = [new StoryLayer("image", 50, 50, 200, Image: "wiki:Tower")] }, null);
        var withNothing = await w.Publisher.RenderAsync(Spec() with { Layers = [new StoryLayer("image", 50, 50, 200, Image: "wiki:Nowhere")] }, null);

        await Assert.That(withMine.IsSuccess).IsTrue();
        using (var bitmap = SKBitmap.Decode(withMine.Value))
            await Assert.That(bitmap.GetPixel(5, 5)).IsEqualTo(SKColors.Green);
        await Assert.That(withTheirs.IsSuccess).IsFalse();
        await Assert.That(withWiki.IsSuccess).IsTrue();
        await Assert.That(withNothing.IsSuccess).IsFalse();
    }

    [Test]
    public async Task StoriesExpireAfter24Hours_AndTheSweeperDeletesThem()
    {
        using var w = await CreateAsync();
        var live = (await w.Publisher.PublishAsync(Spec("live"), w.H.User.Id, null)).Value!;
        var old = new Story { AuthorBotId = w.H.Bots[0].Id, Spec = StorySpecRules.Serialize(Spec("old")), SizeBytes = 1, CreatedAt = DateTime.UtcNow.AddHours(-25), ExpiresAt = DateTime.UtcNow.AddHours(-1) };
        w.H.Db.Stories.Add(old);
        await w.H.Db.SaveChangesAsync();
        await w.Storage.SaveStoryFileAsync(old.Id, [1]);

        var rings = (await new GetStoriesHandler(w.H.Db).Handle(new GetStoriesQuery(w.H.User.Id), default)).Value!;
        await Assert.That(rings.SelectMany(r => r.Stories).Select(s => s.Id)).IsEquivalentTo(new[] { live.Id });
        await Assert.That(rings.Single().IsMine).IsTrue();
        await Assert.That((await new GetStoryImageHandler(w.H.Db, w.Storage).Handle(new GetStoryImageQuery(old.Id), default)).IsSuccess).IsFalse();

        var services = new ServiceCollection();
        services.AddScoped(_ => w.H.Provider.GetRequiredService<NeuralDamageDbContext>());
        services.AddSingleton<NeuralDamage.Infrastructure.Services.Attachments.IAttachmentStorage>(w.Storage);
        using var provider = services.BuildServiceProvider();
        var swept = await new StorySweeper(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StorySweeper>.Instance).SweepAsync();

        await Assert.That(swept.Stories).IsEqualTo(1);
        await Assert.That(w.Storage.StoryFiles.ContainsKey(old.Id)).IsFalse();
        await Assert.That(w.Storage.StoryFiles.ContainsKey(live.Id)).IsTrue();
    }

    [Test]
    public async Task SeenIsKeptPerPerson()
    {
        using var w = await CreateAsync();
        var story = (await w.Publisher.PublishAsync(Spec(), null, w.H.Bots[0].Id)).Value!;
        var bob = new User { ExternalId = "ext-2", Email = "bob@example.com", DisplayName = "Bob" };
        w.H.Db.Users.Add(bob);
        await w.H.Db.SaveChangesAsync();

        await new MarkStorySeenHandler(w.H.Db).Handle(new MarkStorySeenCommand(w.H.User.Id, story.Id), default);
        await new MarkStorySeenHandler(w.H.Db).Handle(new MarkStorySeenCommand(w.H.User.Id, story.Id), default);

        var alices = (await new GetStoriesHandler(w.H.Db).Handle(new GetStoriesQuery(w.H.User.Id), default)).Value!;
        var bobs = (await new GetStoriesHandler(w.H.Db).Handle(new GetStoriesQuery(bob.Id), default)).Value!;
        await Assert.That(alices.Single().AllSeen).IsTrue();
        await Assert.That(bobs.Single().AllSeen).IsFalse();
        await Assert.That(await w.H.Db.StoryViews.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task AReply_GoesToADirectChatWithTheBot_MadeOnce_WithTheStoryAttached()
    {
        using var w = await CreateAsync();
        var bot = w.H.Bots[0];
        var story = (await w.Publisher.PublishAsync(Spec("kimchi day"), null, bot.Id)).Value!;
        w.Sender.Send(Arg.Any<SendMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        var handler = new ReplyToStoryHandler(w.H.Db, w.Storage, w.Sender);

        var first = await handler.Handle(new ReplyToStoryCommand(w.H.User.Id, story.Id, "looks great"), default);
        var second = await handler.Handle(new ReplyToStoryCommand(w.H.User.Id, story.Id, "🔥"), default);

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(second.Value!.ChatId).IsEqualTo(first.Value!.ChatId);
        var chat = await w.H.Db.Chats.Include(c => c.Members).SingleAsync(c => c.Id == first.Value.ChatId);
        await Assert.That(chat.Name).IsEqualTo(bot.Name);
        await Assert.That(chat.Members.Select(m => m.BotId ?? m.UserId)).IsEquivalentTo(new Guid?[] { w.H.User.Id, bot.Id });
        var attachment = await w.H.Db.Attachments.FirstAsync(a => a.ChatId == chat.Id);
        await Assert.That(attachment.Description).Contains("kimchi day");
        await w.Sender.Received(1).Send(Arg.Is<SendMessageCommand>(c => c.ChatId == chat.Id && c.Content == "looks great" && c.AttachmentIds!.Count == 1), Arg.Any<CancellationToken>());
        await w.ChatNotifications.Received(1).NotifyUserChatCreated(w.H.User.Id, Arg.Any<ChatDto>());
    }

    [Test]
    public async Task AReply_KeepsWhatTheStorySaid_AfterTheStoryIsGone()
    {
        using var w = await CreateAsync();
        var bot = w.H.Bots[0];
        var story = (await w.Publisher.PublishAsync(Spec("kimchi day"), null, bot.Id)).Value!;
        var send = new SendMessageHandler(w.H.Db, w.ChatNotifications, Substitute.For<IBotResponseOrchestrator>(), Substitute.For<IBotResponseQueue>(), Substitute.For<ISender>(), new ChatBotState());
        w.Sender.Send(Arg.Any<SendMessageCommand>(), Arg.Any<CancellationToken>()).Returns(call => send.Handle(call.Arg<SendMessageCommand>(), default));

        var replied = await new ReplyToStoryHandler(w.H.Db, w.Storage, w.Sender)
            .Handle(new ReplyToStoryCommand(w.H.User.Id, story.Id, "💀"), default);
        await Assert.That(replied.IsSuccess).IsTrue();

        // The story expires and is swept away, file and all.
        w.H.Db.Stories.Remove(await w.H.Db.Stories.SingleAsync(s => s.Id == story.Id));
        await w.H.Db.SaveChangesAsync();
        w.Storage.DeleteStoryFile(story.Id);
        w.H.Db.ChangeTracker.Clear();

        var message = await w.H.Db.Messages.Include(m => m.SenderUser).Include(m => m.Attachments).SingleAsync(m => m.ChatId == replied.Value!.ChatId);
        await Assert.That(message.StoryId).IsEqualTo(story.Id);
        await Assert.That(message.StoryAuthorBotId).IsEqualTo(bot.Id);
        await Assert.That(message.StoryText).IsEqualTo("kimchi day / [sticker red-square]");
        await Assert.That(message.Attachments).HasSingleItem();
        await Assert.That(message.ToDto().Story!.AuthorName).IsEqualTo(bot.Name);
        await Assert.That(BotPromptBuilder.BuildHistory([message], bot.Id).Single().Content)
            .IsEqualTo("[Alice replied to your story (\"kimchi day / [sticker red-square]\")] (you're answering this): 💀");
    }

    [Test]
    public async Task ABotWritesItsStorySpec_AndAnInvalidOneIsDropped()
    {
        using var w = await CreateAsync();
        var bot = w.H.Bots[0];
        var writer = new StoryWriter(w.H.Db, w.H.OpenRouter,
            new FeedPostWriter(w.H.Db, w.H.OpenRouter, new ConfigurationBuilder().Build(), NullLogger<FeedPostWriter>.Instance),
            w.Publisher, NullLogger<StoryWriter>.Instance);
        string? system = null;
        w.H.OpenRouter.GenerateResponseAsync(default!, default, default!, default!, default).ReturnsForAnyArgs(ci =>
        {
            system = ci.ArgAt<string>(2);
            return """Here it is: {"background":{"color":"#3f2a1d"},"layers":[{"type":"text","text":"Productivity is up 400%","x":50,"y":40,"size":30,"color":"#ffffff","weight":700},{"type":"sticker","name":"red-square","x":50,"y":75,"size":90}]}""";
        });

        var story = await writer.WriteAsync(bot, DateTimeOffset.UtcNow);

        await Assert.That(story).IsNotNull();
        await Assert.That(story!.Author.Id).IsEqualTo(bot.Id);
        await Assert.That(system!).Contains("red-square: a red square");
        await Assert.That(system!).Contains(bot.SystemPrompt);

        w.H.Reply("""{"background":{"color":"#000000"},"layers":[{"type":"sticker","name":"not-in-the-library","x":50,"y":50,"size":90}]}""");
        await Assert.That(await writer.WriteAsync(bot, DateTimeOffset.UtcNow)).IsNull();
        w.H.Reply("I would rather not.");
        await Assert.That(await writer.WriteAsync(bot, DateTimeOffset.UtcNow)).IsNull();
        await Assert.That(await w.H.Db.Stories.CountAsync()).IsEqualTo(1);
    }
}
