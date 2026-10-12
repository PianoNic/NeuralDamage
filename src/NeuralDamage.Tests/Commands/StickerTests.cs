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
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.Stickers;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

/// <summary>The shared sticker library: uploads, who may change what, and the describer.</summary>
public class StickerTests
{
    private sealed record Setup(NeuralDamageDbContext Db, User Uploader, User Other, User Admin, InMemoryAttachmentStorage Storage, IStickerDescribeQueue Queue, AppAdmins Admins) : IDisposable
    {
        public UploadStickerHandler Upload() => new(Db, Storage, Queue);
        public ImportStickersHandler Import() => new(Db, Storage, Queue, Admins);
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
        return new Setup(db, uploader, other, admin, new InMemoryAttachmentStorage(), Substitute.For<IStickerDescribeQueue>(), new AppAdmins(["admin@test.com"]));
    }

    private static int _pngs;

    /// <summary>A PNG no other call returns, so uploads in a test are not duplicates of each other.</summary>
    private static byte[] Png() => TestImages.Png(1 + Interlocked.Increment(ref _pngs) % 500, 32);

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
        s.Queue.Received(1).Enqueue(sticker.Id);

        var image = await new GetStickerImageHandler(s.Storage).Handle(new GetStickerImageQuery(sticker.Id), CancellationToken.None);
        await Assert.That(image.Value!.ContentType).IsEqualTo("image/png");
    }

    [Test]
    public async Task Upload_WithATakenName_GetsANumber()
    {
        using var s = await CreateAsync();
        var first = await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "wow", Png()), CancellationToken.None);
        var second = await s.Upload().Handle(new UploadStickerCommand(s.Other.Id, "WOW!", Png()), CancellationToken.None);

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
        var sticker = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", Png()), CancellationToken.None)).Value!;

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
        var cat = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", Png()), CancellationToken.None)).Value!;
        await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "dog", Png()), CancellationToken.None);

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
        var sticker = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", Png()), CancellationToken.None)).Value!;

        var update = await s.Update().Handle(new UpdateStickerCommand(sticker.Id, s.Other.Id, "mine", "x"), CancellationToken.None);
        var delete = await s.Delete().Handle(new DeleteStickerCommand(sticker.Id, s.Other.Id), CancellationToken.None);
        await Assert.That(update.IsFailure).IsTrue();
        await Assert.That(delete.IsFailure).IsTrue();

        var list = (await new GetStickersHandler(s.Db, s.Admins).Handle(new GetStickersQuery(s.Other.Id), CancellationToken.None)).Value!.Items;
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
        var sticker = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", Png()), CancellationToken.None)).Value!;

        await s.Delete().Handle(new DeleteStickerCommand(sticker.Id, s.Uploader.Id), CancellationToken.None);

        var list = (await new GetStickersHandler(s.Db, s.Admins).Handle(new GetStickersQuery(s.Uploader.Id), CancellationToken.None)).Value!.Items;
        await Assert.That(list).IsEmpty();
        var image = await new GetStickerImageHandler(s.Storage).Handle(new GetStickerImageQuery(sticker.Id), CancellationToken.None);
        await Assert.That(image.IsSuccess).IsTrue();
        // Its name is free again.
        var again = await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", Png()), CancellationToken.None);
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
        var a = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "a", Png()), CancellationToken.None)).Value!;
        var b = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "b", Png()), CancellationToken.None)).Value!;
        var now = DateTime.UtcNow;
        s.Db.Messages.AddRange(
            new Message { ChatId = chat.Id, SenderUserId = s.Uploader.Id, Content = "", StickerId = a.Id, CreatedAt = now.AddMinutes(-3) },
            new Message { ChatId = chat.Id, SenderUserId = s.Uploader.Id, Content = "", StickerId = b.Id, CreatedAt = now.AddMinutes(-2) },
            new Message { ChatId = chat.Id, SenderUserId = s.Uploader.Id, Content = "", StickerId = a.Id, CreatedAt = now.AddMinutes(-1) },
            new Message { ChatId = chat.Id, SenderUserId = s.Other.Id, Content = "", StickerId = b.Id, CreatedAt = now });
        await s.Db.SaveChangesAsync();

        var recent = (await new GetRecentStickersHandler(s.Db, s.Admins).Handle(new GetRecentStickersQuery(s.Uploader.Id), CancellationToken.None)).Value!.Select(r => r.Id).ToList();

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

    /// <summary>An animated WebP: VP8X with the animation flag, then ANIM with <paramref name="loops"/>.</summary>
    private static byte[] AnimatedWebP(ushort loops)
    {
        var data = new List<byte>();
        data.AddRange("RIFF"u8.ToArray());
        data.AddRange(new byte[4]);
        data.AddRange("WEBP"u8.ToArray());
        data.AddRange("VP8X"u8.ToArray());
        data.AddRange([10, 0, 0, 0, 0x02, 0, 0, 0, 63, 0, 0, 63, 0, 0]);
        data.AddRange("ANIM"u8.ToArray());
        data.AddRange([6, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, (byte)loops, (byte)(loops >> 8)]);
        data.AddRange("ANMF"u8.ToArray());
        data.AddRange([2, 0, 0, 0, 0, 0]);
        var bytes = data.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        return bytes;
    }

    /// <summary>A two-frame GIF with a NETSCAPE2.0 extension saying <paramref name="loops"/>.</summary>
    private static byte[] LoopingGif(ushort loops)
    {
        var gif = Gif(64, 64, frames: 2);
        byte[] extension = [0x21, 0xFF, 0x0B, .. "NETSCAPE2.0"u8, 0x03, 0x01, (byte)loops, (byte)(loops >> 8), 0x00];
        return [.. gif.AsSpan(0, 13), .. extension, .. gif.AsSpan(13)];
    }

    [Test]
    public async Task AnimatedWebP_ThatPlaysOnce_IsStoredLoopingForever()
    {
        using var s = await CreateAsync();
        var once = AnimatedWebP(loops: 1);
        await Assert.That(StickerRules.Inspect(once)!.Value.Animated).IsTrue();

        var sticker = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "wave", once), CancellationToken.None)).Value!;

        var stored = s.Storage.Stickers[sticker.Id];
        await Assert.That(stored.Length).IsEqualTo(once.Length);
        await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(stored.AsSpan(42))).IsEqualTo((ushort)0);
        // Nothing else changed.
        await Assert.That(stored.SequenceEqual(AnimatedWebP(loops: 0))).IsTrue();
        await Assert.That(StickerRules.LoopForever(AnimatedWebP(loops: 0)).SequenceEqual(AnimatedWebP(loops: 0))).IsTrue();
    }

    [Test]
    public async Task AnimatedGif_ThatPlaysOnce_IsStoredLoopingForever()
    {
        using var s = await CreateAsync();

        var sticker = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "dance", LoopingGif(loops: 1)), CancellationToken.None)).Value!;

        var stored = s.Storage.Stickers[sticker.Id];
        await Assert.That(stored.SequenceEqual(LoopingGif(loops: 0))).IsTrue();
        await Assert.That(StickerRules.Inspect(stored)!.Value.Animated).IsTrue();
    }

    [Test]
    public async Task AnimatedGif_WithoutALoopExtension_GetsOne()
    {
        var plain = Gif(64, 64, frames: 2);

        var looped = StickerRules.LoopForever(plain);

        await Assert.That(looped.SequenceEqual(LoopingGif(loops: 0))).IsTrue();
        await Assert.That(looped.Length).IsEqualTo(plain.Length + 19);
        await Assert.That(StickerRules.Inspect(looped)!.Value.Animated).IsTrue();
        // A still GIF is left alone.
        var still = Gif(64, 64, frames: 1);
        await Assert.That(StickerRules.LoopForever(still).SequenceEqual(still)).IsTrue();
    }

    [Test]
    public async Task Upload_RefusesAnImageTheLibraryAlreadyHas_UntilItIsDeleted()
    {
        using var s = await CreateAsync();
        var png = Png();
        var first = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "cat", png), CancellationToken.None)).Value!;

        var again = await s.Upload().Handle(new UploadStickerCommand(s.Other.Id, "other-name", png.ToArray()), CancellationToken.None);
        await Assert.That(again.IsFailure).IsTrue();
        await Assert.That(again.Error).Contains("cat");

        await s.Delete().Handle(new DeleteStickerCommand(first.Id, s.Uploader.Id), CancellationToken.None);
        var afterDelete = await s.Upload().Handle(new UploadStickerCommand(s.Other.Id, "cat", png), CancellationToken.None);
        await Assert.That(afterDelete.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Import_SkipsDuplicates_KeepsGoingPastBadFiles_AndQueuesEachNewOne()
    {
        using var s = await CreateAsync();
        var existing = Png();
        await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "old", existing), CancellationToken.None);
        var a = Png();
        var b = Gif(512, 512, frames: 2);

        var result = await s.Import().Handle(new ImportStickersCommand(s.Admin.Id,
        [
            new("STK-0001.webp", a),
            new("STK-0002.webp", a.ToArray()), // the same image twice in one import
            new("STK-0003.gif", b),
            new("STK-0004.png", existing.ToArray()), // already in the library
            new("photo.jpg", TestImages.Jpeg(64, 64)),
        ]), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!.Added).IsEqualTo(2);
        await Assert.That(result.Value.Duplicates).IsEqualTo(2);
        await Assert.That(result.Value.Failed.Single().FileName).IsEqualTo("photo.jpg");

        var stored = await s.Db.Stickers.Where(x => x.UploaderUserId == s.Admin.Id).OrderBy(x => x.Name).ToListAsync();
        await Assert.That(stored.Select(x => x.Name)).IsEquivalentTo(["stk-0001", "stk-0003"]);
        await Assert.That(stored.All(x => x.Sha256 == StickerRules.Hash(s.Storage.Stickers[x.Id]))).IsTrue();
        foreach (var sticker in stored)
            s.Queue.Received(1).Enqueue(sticker.Id);
    }

    [Test]
    public async Task Import_IsForAdminsOnly()
    {
        using var s = await CreateAsync();

        var result = await s.Import().Handle(new ImportStickersCommand(s.Other.Id, [new("a.png", Png())]), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error).IsEqualTo(ImportStickersHandler.AdminsOnly);
        await Assert.That(await s.Db.Stickers.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task OlderStickers_GetTheirHashFilledIn_SoImportsSkipThem()
    {
        using var s = await CreateAsync();
        var png = Png();
        var old = new Sticker { Name = "old", ContentType = "image/png", SizeBytes = png.Length, UploaderUserId = s.Uploader.Id };
        s.Db.Stickers.Add(old);
        await s.Db.SaveChangesAsync();
        await s.Storage.SaveStickerAsync(old.Id, png);

        await StickerDescribeService.BackfillHashesAsync(s.Db, s.Storage, NullLogger.Instance, CancellationToken.None);

        await Assert.That(old.Sha256).IsEqualTo(StickerRules.Hash(png));
        var result = await s.Import().Handle(new ImportStickersCommand(s.Admin.Id, [new("again.png", png)]), CancellationToken.None);
        await Assert.That(result.Value!.Duplicates).IsEqualTo(1);
    }

    [Test]
    public async Task Library_IsReadAPageAtATime_NewestFirst_AndSearchedOnTheServer()
    {
        using var s = await CreateAsync();
        var start = DateTime.UtcNow.AddDays(-1);
        s.Db.Stickers.AddRange(Enumerable.Range(0, 130).Select(i => new Sticker
        {
            Name = i == 7 ? "cat-lol" : $"s{i:000}",
            Description = i % 10 == 0 ? $"dog laughing so hard, number {i}" : "a plain sticker",
            ContentType = "image/png",
            SizeBytes = 10,
            UploaderUserId = s.Uploader.Id,
            CreatedAt = start.AddMinutes(i),
            DeletedAt = i == 129 ? DateTime.UtcNow : null,
        }));
        await s.Db.SaveChangesAsync();
        var handler = new GetStickersHandler(s.Db, s.Admins);
        async Task<StickerPageDto> Page(string? search, int skip, int take) =>
            (await handler.Handle(new GetStickersQuery(s.Other.Id, search, skip, take), CancellationToken.None)).Value!;

        var first = await Page(null, 0, 50);
        await Assert.That(first.Total).IsEqualTo(129);
        await Assert.That(first.Items.Count).IsEqualTo(50);
        await Assert.That(first.Items[0].Name).IsEqualTo("s128");
        var last = await Page(null, 100, 50);
        await Assert.That(last.Items.Count).IsEqualTo(29);
        await Assert.That(last.Items[^1].Name).IsEqualTo("s000");
        await Assert.That(first.Items.Select(x => x.Id).Intersect(last.Items.Select(x => x.Id))).IsEmpty();

        var laughing = await Page("Dog LAUGHING", 0, 50);
        await Assert.That(laughing.Total).IsEqualTo(13);
        await Assert.That(laughing.Items.All(x => x.Description!.StartsWith("dog laughing"))).IsTrue();
        var byName = await Page("cat lol", 0, 50);
        await Assert.That(byName.Items.Single().Name).IsEqualTo("cat-lol");

        // Asked for nothing, a page still holds one; asked for everything, it stops at the cap.
        await Assert.That((await Page(null, 0, 0)).Items.Count).IsEqualTo(1);
        await Assert.That((await Page(null, 0, 10_000)).Items.Count).IsEqualTo(Math.Min(129, GetStickersQuery.MaxTake));
    }

    [Test]
    public async Task Status_CountsTheLibrary_WhatIsStillBeingDescribed_AndWhoMayImport()
    {
        using var s = await CreateAsync();
        await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "a", Png()), CancellationToken.None);
        var b = (await s.Upload().Handle(new UploadStickerCommand(s.Uploader.Id, "b", Png()), CancellationToken.None)).Value!;
        await s.Update().Handle(new UpdateStickerCommand(b.Id, s.Uploader.Id, "b", "described"), CancellationToken.None);

        var forOther = (await new GetStickerLibraryStatusHandler(s.Db, s.Admins).Handle(new GetStickerLibraryStatusQuery(s.Other.Id), CancellationToken.None)).Value!;
        var forAdmin = (await new GetStickerLibraryStatusHandler(s.Db, s.Admins).Handle(new GetStickerLibraryStatusQuery(s.Admin.Id), CancellationToken.None)).Value!;

        await Assert.That(forOther).IsEqualTo(new StickerLibraryStatusDto(2, 1, false));
        await Assert.That(forAdmin.CanImport).IsTrue();
    }
}
