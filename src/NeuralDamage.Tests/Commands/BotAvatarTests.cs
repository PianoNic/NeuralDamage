using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class BotAvatarTests
{
    private static async Task<(NeuralDamage.Infrastructure.NeuralDamageDbContext db, User creator, User other, Bot bot)> Setup()
    {
        var db = TestDbContext.Create();
        var creator = new User { ExternalId = "ext-1", Email = "creator@test.com", DisplayName = "Creator" };
        var other = new User { ExternalId = "ext-2", Email = "other@test.com", DisplayName = "Other" };
        db.Users.AddRange(creator, other);
        var bot = new Bot { Name = "Gus", ModelId = "deepseek/deepseek-chat", SystemPrompt = "X", CreatedById = creator.Id };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();
        return (db, creator, other, bot);
    }

    [Test]
    public async Task Creator_SetsPicture_AndEveryoneCanLoadIt()
    {
        var (db, creator, _, bot) = await Setup();
        using var _ = db;
        var storage = new InMemoryAttachmentStorage();
        var png = TestImages.Png(256, 256);

        var result = await new SetBotAvatarHandler(db, storage, new AttachmentOptions())
            .Handle(new SetBotAvatarCommand(bot.Id, creator.Id, png), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!).StartsWith($"/api/bots/{bot.Id}/avatar?v=");
        await Assert.That(bot.AvatarUrl).IsEqualTo(result.Value);

        var file = await new GetBotAvatarHandler(storage).Handle(new GetBotAvatarQuery(bot.Id), CancellationToken.None);
        await Assert.That(file.IsSuccess).IsTrue();
        await Assert.That(file.Value!.ContentType).IsEqualTo("image/png");
        await Assert.That(file.Value.Content).IsEquivalentTo(png);
    }

    [Test]
    public async Task NewPicture_ReplacesTheOldOne_UnderANewUrl()
    {
        var (db, creator, _, bot) = await Setup();
        using var _ = db;
        var storage = new InMemoryAttachmentStorage();
        var handler = new SetBotAvatarHandler(db, storage, new AttachmentOptions());

        var first = await handler.Handle(new SetBotAvatarCommand(bot.Id, creator.Id, TestImages.Png()), CancellationToken.None);
        await Task.Delay(2);
        var second = await handler.Handle(new SetBotAvatarCommand(bot.Id, creator.Id, TestImages.Gif(64, 64)), CancellationToken.None);

        await Assert.That(second.Value).IsNotEqualTo(first.Value);
        await Assert.That(storage.BotAvatars[bot.Id]).IsEquivalentTo(TestImages.Gif(64, 64));
    }

    [Test]
    public async Task SomeoneElse_CannotChangeIt()
    {
        var (db, _, other, bot) = await Setup();
        using var _ = db;
        var storage = new InMemoryAttachmentStorage();

        var set = await new SetBotAvatarHandler(db, storage, new AttachmentOptions())
            .Handle(new SetBotAvatarCommand(bot.Id, other.Id, TestImages.Png()), CancellationToken.None);
        var remove = await new RemoveBotAvatarHandler(db, storage)
            .Handle(new RemoveBotAvatarCommand(bot.Id, other.Id), CancellationToken.None);

        await Assert.That(set.IsFailure).IsTrue();
        await Assert.That(remove.IsFailure).IsTrue();
        await Assert.That(storage.BotAvatars).IsEmpty();
        await Assert.That(bot.AvatarUrl).IsNull();
    }

    [Test]
    [Arguments(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x3E })]
    [Arguments(new byte[0])]
    public async Task NotAnImage_IsRefused(byte[] data)
    {
        var (db, creator, _, bot) = await Setup();
        using var _ = db;
        var storage = new InMemoryAttachmentStorage();

        var result = await new SetBotAvatarHandler(db, storage, new AttachmentOptions())
            .Handle(new SetBotAvatarCommand(bot.Id, creator.Id, data), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(storage.BotAvatars).IsEmpty();
    }

    [Test]
    public async Task TooBig_IsRefused()
    {
        var (db, creator, _, bot) = await Setup();
        using var _ = db;
        var png = TestImages.Png();

        var result = await new SetBotAvatarHandler(db, new InMemoryAttachmentStorage(), new AttachmentOptions { MaxBytes = png.Length - 1 })
            .Handle(new SetBotAvatarCommand(bot.Id, creator.Id, png), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task Removing_DeletesTheFile_AndFallsBackToTheModelIcon()
    {
        var (db, creator, _, bot) = await Setup();
        using var _ = db;
        var storage = new InMemoryAttachmentStorage();
        await new SetBotAvatarHandler(db, storage, new AttachmentOptions())
            .Handle(new SetBotAvatarCommand(bot.Id, creator.Id, TestImages.Png()), CancellationToken.None);

        var result = await new RemoveBotAvatarHandler(db, storage)
            .Handle(new RemoveBotAvatarCommand(bot.Id, creator.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(bot.AvatarUrl).IsNull();
        await Assert.That(storage.BotAvatars).IsEmpty();
        var file = await new GetBotAvatarHandler(storage).Handle(new GetBotAvatarQuery(bot.Id), CancellationToken.None);
        await Assert.That(file.IsFailure).IsTrue();
    }

    [Test]
    public async Task DeletingTheBot_DeletesItsPicture()
    {
        var (db, creator, _, bot) = await Setup();
        using var _ = db;
        var storage = new InMemoryAttachmentStorage();
        await new SetBotAvatarHandler(db, storage, new AttachmentOptions())
            .Handle(new SetBotAvatarCommand(bot.Id, creator.Id, TestImages.Png()), CancellationToken.None);

        await new DeleteBotHandler(db, Substitute.For<IChatNotificationService>(), storage)
            .Handle(new DeleteBotCommand(bot.Id, creator.Id), CancellationToken.None);

        await Assert.That(bot.AvatarUrl).IsNull();
        await Assert.That(storage.BotAvatars).IsEmpty();
    }
}
