using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Application.Validators;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Stickers;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

/// <summary>The shared sticker library: uploads, who may change what, and the describer.</summary>
public class StickerTests
{
    private sealed record Setup(NeuralDamageDbContext Db, User Uploader, User Other, User Admin, InMemoryAttachmentStorage Storage, IImageDescriber Describer, AppAdmins Admins) : IDisposable
    {
        public UploadStickerHandler Upload() => new(Db, Storage, Describer);
        public UpdateStickerHandler Update() => new(Db, Admins);
        public DeleteStickerHandler Delete() => new(Db, Admins);
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Setup> CreateAsync()
    {
        var db = TestDbContext.Create();
        var uploader = new User { ExternalId = "ext-1", Email = "uploader@test.com", DisplayName = "Uploader" };
        var other = new User { ExternalId = "ext-2", Email = "other@test.com", DisplayName = "Other" };
        var admin = new User { ExternalId = "ext-3", Email = "Admin@Test.com", DisplayName = "Admin" };
        db.Users.AddRange(uploader, other, admin);
        await db.SaveChangesAsync();
        return new Setup(db, uploader, other, admin, new InMemoryAttachmentStorage(), Substitute.For<IImageDescriber>(), new AppAdmins(["admin@test.com"]));
    }

    /// <summary>A GIF with <paramref name="frames"/> one-pixel frames.</summary>
    private static byte[] Gif(int width, int height, int frames)
    {
        var data = new List<byte>();
        data.AddRange("GIF89a"u8.ToArray());
        var size = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(size, (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(size.AsSpan(2), (ushort)height);
        data.AddRange(size);
        data.AddRange([0x00, 0x00, 0x00]);
        for (var i = 0; i < frames; i++)
        {
            data.AddRange([0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00]);
            data.AddRange([0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0x00]);
            data.AddRange([0x02, 0x02, 0x44, 0x01, 0x00]);
        }
        data.Add(0x3B);
        return [.. data];
    }

    [Test]
    public async Task Upload_StoresIt_NamesItFromTheFile_AndStartsTheDescriber()
    {
        using var s = await CreateAsync();
        var png = TestImages.Png(512, 400);

        var result = await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "Cat Laughing", png), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        var sticker = result.Value!;
        await Assert.That(sticker.Name).IsEqualTo("cat-laughing");
        await Assert.That(sticker.ContentType).IsEqualTo("image/png");
        await Assert.That(sticker.Animated).IsFalse();
        await Assert.That(sticker.Describing).IsTrue();
        await Assert.That(sticker.Url).IsEqualTo($"/api/stickers/{sticker.Id}/image");
        await Assert.That(s.Storage.Stickers[sticker.Id]).IsEquivalentTo(png);
        s.Describer.Received(1).EnqueueSticker(sticker.Id);

        var image = await new GetStickerImageHandler(s.Storage).Handle(new GetStickerImageQuery(sticker.Id), CancellationToken.None);
        await Assert.That(image.Value!.ContentType).IsEqualTo("image/png");
    }

    [Test]
    public async Task Upload_WithATakenName_GetsANumber()
    {
        using var s = await CreateAsync();
        var first = await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "wow", TestImages.Png()), CancellationToken.None);
        var second = await s.Upload().Handle(new UploadStickerCommand(s.Other.Id, "WOW!", TestImages.Png()), CancellationToken.None);

        await Assert.That(first.Value!.Name).IsEqualTo("wow");
        await Assert.That(second.Value!.Name).IsEqualTo("wow-2");
    }

    [Test]
    public async Task AnimatedGif_KeepsItsSize_AndIsMarkedAnimated()
    {
        using var s = await CreateAsync();

        var result = await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "dance", Gif(800, 600, frames: 3)), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!.Animated).IsTrue();
    }

    [Test]
    public async Task SingleFrameGif_IsStill()
    {
        await Assert.That(StickerRules.Inspect(Gif(64, 64, frames: 1))!.Value.Animated).IsFalse();
        await Assert.That(StickerRules.Inspect(Gif(64, 64, frames: 2))!.Value.Animated).IsTrue();
    }

    [Test]
    public async Task Upload_Refuses_WhatIsNotAStickerImage()
    {
        using var s = await CreateAsync();
        byte[][] refused =
        [
            [],
            [0x3C, 0x73, 0x76, 0x67, 0x3E], // "<svg>"
            TestImages.Jpeg(64, 64), // not one of PNG, WebP, GIF
            TestImages.Png(1024, 512), // a still one the browser should have scaled
            Gif(2048, 2048, frames: 2), // too big even animated
            new byte[StickerRules.MaxBytes + 1],
        ];

        foreach (var data in refused)
        {
            var result = await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "x", data), CancellationToken.None);
            await Assert.That(result.IsFailure).IsTrue();
        }
        await Assert.That(s.Storage.Stickers).IsEmpty();
        await Assert.That(await s.Db.Stickers.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Uploader_CanRenameAndDescribe_AnEmptyDescriptionHidesItAgain()
    {
        using var s = await CreateAsync();
        var sticker = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", TestImages.Png()), CancellationToken.None)).Value!;

        var described = await s.Update().Handle(new UpdateStickerCommand(sticker.Id, s.Uploader.Id, "cat-lol", "  cat crying with laughter  "), CancellationToken.None);
        await Assert.That(described.Value!.Name).IsEqualTo("cat-lol");
        await Assert.That(described.Value.Description).IsEqualTo("cat crying with laughter");
        await Assert.That(described.Value.Describing).IsFalse();

        var cleared = await s.Update().Handle(new UpdateStickerCommand(sticker.Id, s.Uploader.Id, "cat-lol", " "), CancellationToken.None);
        await Assert.That(cleared.Value!.Description).IsNull();
    }

    [Test]
    public async Task Update_RefusesABadOrTakenName_AndALongDescription()
    {
        using var s = await CreateAsync();
        var cat = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", TestImages.Png()), CancellationToken.None)).Value!;
        await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "dog", TestImages.Png()), CancellationToken.None);

        foreach (var (name, description) in new[] { ("dog", "x"), ("Cat Face", "x"), ("", "x"), ("cat", new string('x', StickerRules.MaxDescriptionLength + 1)) })
        {
            var result = await s.Update().Handle(new UpdateStickerCommand(cat.Id, s.Uploader.Id, name, description), CancellationToken.None);
            await Assert.That(result.IsFailure).IsTrue();
        }
    }

    [Test]
    public async Task SomeoneElse_CannotChangeOrDeleteIt_ButAnAdminCan()
    {
        using var s = await CreateAsync();
        var sticker = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", TestImages.Png()), CancellationToken.None)).Value!;

        var update = await s.Update().Handle(new UpdateStickerCommand(sticker.Id, s.Other.Id, "mine", "x"), CancellationToken.None);
        var delete = await s.Delete().Handle(new DeleteStickerCommand(sticker.Id, s.Other.Id), CancellationToken.None);
        await Assert.That(update.IsFailure).IsTrue();
        await Assert.That(delete.IsFailure).IsTrue();

        var list = (await new GetStickersHandler(s.Db, s.Admins).Handle(new GetStickersQuery(s.Other.Id), CancellationToken.None)).Value!;
        await Assert.That(list.Single().CanEdit).IsFalse();

        var byAdmin = await s.Update().Handle(new UpdateStickerCommand(sticker.Id, s.Admin.Id, "cat", "cat, tidied up by the admin"), CancellationToken.None);
        await Assert.That(byAdmin.IsSuccess).IsTrue();
        var deleted = await s.Delete().Handle(new DeleteStickerCommand(sticker.Id, s.Admin.Id), CancellationToken.None);
        await Assert.That(deleted.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Deleting_TakesItOutOfTheLibrary_ButOldMessagesStillShowIt()
    {
        using var s = await CreateAsync();
        var sticker = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", TestImages.Png()), CancellationToken.None)).Value!;

        await s.Delete().Handle(new DeleteStickerCommand(sticker.Id, s.Uploader.Id), CancellationToken.None);

        var list = (await new GetStickersHandler(s.Db, s.Admins).Handle(new GetStickersQuery(s.Uploader.Id), CancellationToken.None)).Value!;
        await Assert.That(list).IsEmpty();
        var image = await new GetStickerImageHandler(s.Storage).Handle(new GetStickerImageQuery(sticker.Id), CancellationToken.None);
        await Assert.That(image.IsSuccess).IsTrue();
        // Its name is free again.
        var again = await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", TestImages.Png()), CancellationToken.None);
        await Assert.That(again.Value!.Name).IsEqualTo("cat");
    }

    [Test]
    public async Task SendingASticker_IsAMessageOfItsOwn()
    {
        var validator = new SendMessageValidator();
        var sticker = Guid.NewGuid();

        var alone = await validator.ValidateAsync(new SendMessageCommand(Guid.NewGuid(), Guid.NewGuid(), "", StickerId: sticker));
        var withText = await validator.ValidateAsync(new SendMessageCommand(Guid.NewGuid(), Guid.NewGuid(), "hi", StickerId: sticker));
        var withImage = await validator.ValidateAsync(new SendMessageCommand(Guid.NewGuid(), Guid.NewGuid(), "", AttachmentIds: [Guid.NewGuid()], StickerId: sticker));

        await Assert.That(alone.IsValid).IsTrue();
        await Assert.That(withText.IsValid).IsFalse();
        await Assert.That(withImage.IsValid).IsFalse();
    }

    [Test]
    public async Task RecentStickers_AreTheOnesYouSentLast_Once_Each()
    {
        using var s = await CreateAsync();
        var chat = new Chat { Name = "Chat", CreatedById = s.Uploader.Id };
        s.Db.Chats.Add(chat);
        var a = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "a", TestImages.Png()), CancellationToken.None)).Value!;
        var b = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "b", TestImages.Png()), CancellationToken.None)).Value!;
        var now = DateTime.UtcNow;
        s.Db.Messages.AddRange(
            new Message { ChatId = chat.Id, SenderUserId = s.Uploader.Id, Content = "", StickerId = a.Id, CreatedAt = now.AddMinutes(-3) },
            new Message { ChatId = chat.Id, SenderUserId = s.Uploader.Id, Content = "", StickerId = b.Id, CreatedAt = now.AddMinutes(-2) },
            new Message { ChatId = chat.Id, SenderUserId = s.Uploader.Id, Content = "", StickerId = a.Id, CreatedAt = now.AddMinutes(-1) },
            new Message { ChatId = chat.Id, SenderUserId = s.Other.Id, Content = "", StickerId = b.Id, CreatedAt = now });
        await s.Db.SaveChangesAsync();

        var recent = (await new GetRecentStickersHandler(s.Db).Handle(new GetRecentStickersQuery(s.Uploader.Id), CancellationToken.None)).Value!;

        await Assert.That(recent).IsEquivalentTo([a.Id, b.Id]);
        await Assert.That(recent[0]).IsEqualTo(a.Id);
    }

    [Test]
    public async Task FailedDescription_LeavesItUsableButUndescribed_AndNotDescribing()
    {
        var dbOptions = new DbContextOptionsBuilder<NeuralDamageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var user = new User { ExternalId = "ext-1", Email = "u@test.com", DisplayName = "U" };
        var sticker = new Sticker { Name = "cat", ContentType = "image/png", SizeBytes = 64, UploaderUserId = user.Id, Describing = true };
        await using (var db = new NeuralDamageDbContext(dbOptions))
        {
            db.AddRange(user, sticker);
            await db.SaveChangesAsync();
        }
        var storage = new InMemoryAttachmentStorage();
        await storage.SaveStickerAsync(sticker.Id, TestImages.Png());

        // An expired key: the catalogue cannot even be read.
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.ListModelsAsync(Arg.Any<CancellationToken>()).Returns<List<NeuralDamage.Infrastructure.Services.OpenRouterModel>>(_ => throw new HttpRequestException("401 Unauthorized"));
        openRouter.ListZdrModelIdsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlySet<string>>(_ => throw new HttpRequestException("401 Unauthorized"));

        var services = new ServiceCollection();
        services.AddScoped(_ => new NeuralDamageDbContext(dbOptions));
        services.AddSingleton<IAttachmentStorage>(storage);
        services.AddSingleton(openRouter);
        services.AddSingleton(new ModelPolicy(ModelPolicy.DefaultMaxPromptPrice, ModelPolicy.DefaultMaxCompletionPrice));
        using var provider = services.BuildServiceProvider();
        var describer = new ImageDescriber(provider.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), NullLogger<ImageDescriber>.Instance);

        await describer.DescribeStickerAsync(sticker.Id);

        await using var check = new NeuralDamageDbContext(dbOptions);
        var stored = await check.Stickers.SingleAsync();
        await Assert.That(stored.Description).IsNull();
        await Assert.That(stored.Describing).IsFalse();
        await Assert.That(new StickerCatalog([stored]).ForPrompt).IsEmpty();
    }

    [Test]
    public async Task Describer_KeepsOneCleanLine()
    {
        await Assert.That(ImageDescriber.CleanStickerLine("\"cat crying with laughter, for when something is hilarious.\"\nExtra"))
            .IsEqualTo("cat crying with laughter, for when something is hilarious");
        await Assert.That(ImageDescriber.CleanStickerLine("  ")).IsNull();
    }
}
