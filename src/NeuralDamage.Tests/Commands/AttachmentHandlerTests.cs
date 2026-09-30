using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Application.Validators;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class AttachmentHandlerTests
{
    private sealed record Setup(NeuralDamageDbContext Db, User Member, User Outsider, Chat Chat, InMemoryAttachmentStorage Storage, IImageDescriber Describer) : IDisposable
    {
        public UploadAttachmentHandler Upload(AttachmentOptions? options = null) => new(Db, Storage, Describer, options ?? new AttachmentOptions());
        public GetAttachmentHandler Get() => new(Db, Storage);
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Setup> CreateAsync()
    {
        var db = TestDbContext.Create();
        var member = new User { ExternalId = "ext-1", Email = "alice@test.com", DisplayName = "Alice" };
        var outsider = new User { ExternalId = "ext-2", Email = "mallory@test.com", DisplayName = "Mallory" };
        var chat = new Chat { Name = "General", CreatedById = member.Id };
        db.AddRange(member, outsider, chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = member.Id, Role = ChatMemberRole.Owner });
        await db.SaveChangesAsync();
        return new Setup(db, member, outsider, chat, new InMemoryAttachmentStorage(), Substitute.For<IImageDescriber>());
    }

    [Test]
    public async Task Upload_StoresTheImage_WithItsSize_AndStartsTheDescriber()
    {
        using var s = await CreateAsync();

        var result = await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, TestImages.Png(640, 480)), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        var dto = result.Value!;
        await Assert.That(dto.ContentType).IsEqualTo("image/png");
        await Assert.That(dto.Width).IsEqualTo(640);
        await Assert.That(dto.Height).IsEqualTo(480);
        await Assert.That(dto.Url).IsEqualTo($"/api/chats/{s.Chat.Id}/attachments/{dto.Id}");
        await Assert.That(s.Storage.Files.ContainsKey((s.Chat.Id, dto.Id))).IsTrue();
        s.Describer.Received(1).Enqueue(dto.Id);
    }

    [Test]
    public async Task Upload_JudgesTheTypeByTheBytes_NotTheName()
    {
        using var s = await CreateAsync();

        var result = await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, "<svg onload=alert(1)>"u8.ToArray()), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error!).Contains("PNG, JPEG, WebP and GIF");
        await Assert.That(s.Storage.Files).IsEmpty();
        await Assert.That(await s.Db.Attachments.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Upload_RefusesAnImageOverTheSizeLimit()
    {
        using var s = await CreateAsync();
        var image = TestImages.Png();

        var result = await s.Upload(new AttachmentOptions { MaxBytes = image.Length - 1 })
            .Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, image), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error!).Contains("at most");
        s.Describer.DidNotReceiveWithAnyArgs().Enqueue(default);
    }

    [Test]
    public async Task Upload_RefusesSomeoneOutsideTheChat()
    {
        using var s = await CreateAsync();

        var result = await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Outsider.Id, TestImages.Png()), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(s.Storage.Files).IsEmpty();
    }

    [Test]
    public async Task Get_ServesMembers_AndNobodyElse()
    {
        using var s = await CreateAsync();
        var uploaded = (await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, TestImages.Gif(3, 4)), CancellationToken.None)).Value!;

        var member = await s.Get().Handle(new GetAttachmentQuery(s.Chat.Id, uploaded.Id, s.Member.Id), CancellationToken.None);
        var outsider = await s.Get().Handle(new GetAttachmentQuery(s.Chat.Id, uploaded.Id, s.Outsider.Id), CancellationToken.None);

        await Assert.That(member.IsSuccess).IsTrue();
        await Assert.That(member.Value!.ContentType).IsEqualTo("image/gif");
        await Assert.That(outsider.IsFailure).IsTrue();
        await Assert.That(outsider.Error).IsEqualTo(GetAttachmentHandler.NotFound);
    }

    [Test]
    public async Task Get_KeepsAnUnsentUpload_ToItsUploader()
    {
        using var s = await CreateAsync();
        s.Db.ChatMembers.Add(new ChatMember { ChatId = s.Chat.Id, UserId = s.Outsider.Id });
        await s.Db.SaveChangesAsync();
        var uploaded = (await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, TestImages.Png()), CancellationToken.None)).Value!;

        var otherMember = await s.Get().Handle(new GetAttachmentQuery(s.Chat.Id, uploaded.Id, s.Outsider.Id), CancellationToken.None);

        await Assert.That(otherMember.IsFailure).IsTrue();
    }

    [Test]
    public async Task Get_DoesNotServeAnImage_ThroughAnotherChatsUrl()
    {
        using var s = await CreateAsync();
        var uploaded = (await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, TestImages.Png()), CancellationToken.None)).Value!;
        var otherChat = new Chat { Name = "Other", CreatedById = s.Outsider.Id };
        s.Db.Chats.Add(otherChat);
        s.Db.ChatMembers.Add(new ChatMember { ChatId = otherChat.Id, UserId = s.Outsider.Id, Role = ChatMemberRole.Owner });
        await s.Db.SaveChangesAsync();

        var result = await s.Get().Handle(new GetAttachmentQuery(otherChat.Id, uploaded.Id, s.Outsider.Id), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task Send_AttachesTheUploads_AndBroadcastsThem()
    {
        using var s = await CreateAsync();
        var first = (await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, TestImages.Png()), CancellationToken.None)).Value!;
        var second = (await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, TestImages.Jpeg(10, 20)), CancellationToken.None)).Value!;
        var notifications = Substitute.For<IChatNotificationService>();

        var result = await SendHandler(s.Db, notifications).Handle(
            new SendMessageCommand(s.Chat.Id, s.Member.Id, "", AttachmentIds: [first.Id, second.Id]), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        var message = await s.Db.Messages.Include(m => m.Attachments).SingleAsync();
        await Assert.That(message.Attachments.Select(a => a.Id)).IsEquivalentTo([first.Id, second.Id]);
        await notifications.Received(1).NotifyMessageNew(s.Chat.Id, Arg.Is<MessageDto>(d => d.Attachments.Count == 2));
    }

    [Test]
    public async Task Send_RefusesSomeoneElsesUpload()
    {
        using var s = await CreateAsync();
        s.Db.ChatMembers.Add(new ChatMember { ChatId = s.Chat.Id, UserId = s.Outsider.Id });
        await s.Db.SaveChangesAsync();
        var theirs = (await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, TestImages.Png()), CancellationToken.None)).Value!;

        var result = await SendHandler(s.Db, Substitute.For<IChatNotificationService>()).Handle(
            new SendMessageCommand(s.Chat.Id, s.Outsider.Id, "mine now", AttachmentIds: [theirs.Id]), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(await s.Db.Messages.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Validation_AllowsAPictureWithoutText_ButNotTooManyPictures()
    {
        var validator = new SendMessageValidator(new AttachmentOptions { MaxPerMessage = 4 });
        var chat = Guid.NewGuid();
        var user = Guid.NewGuid();

        var pictureOnly = await validator.ValidateAsync(new SendMessageCommand(chat, user, "", AttachmentIds: [Guid.NewGuid()]));
        var nothing = await validator.ValidateAsync(new SendMessageCommand(chat, user, "", AttachmentIds: []));
        var five = await validator.ValidateAsync(new SendMessageCommand(chat, user, "look", AttachmentIds: [.. Enumerable.Range(0, 5).Select(_ => Guid.NewGuid())]));

        await Assert.That(pictureOnly.IsValid).IsTrue();
        await Assert.That(nothing.IsValid).IsFalse();
        await Assert.That(five.IsValid).IsFalse();
        await Assert.That(five.Errors.Single().ErrorMessage).Contains("at most 4 images");
    }

    [Test]
    public async Task ClearingAChat_DeletesItsFiles()
    {
        using var s = await CreateAsync();
        var uploaded = (await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, TestImages.Png()), CancellationToken.None)).Value!;

        var result = await new ClearChatHandler(s.Db, Substitute.For<IChatNotificationService>(), s.Storage)
            .Handle(new ClearChatCommand(s.Chat.Id, s.Member.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(s.Storage.Files.ContainsKey((s.Chat.Id, uploaded.Id))).IsFalse();
        await Assert.That(await s.Db.Attachments.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task DeletingAChat_DeletesItsFiles()
    {
        using var s = await CreateAsync();
        await s.Upload().Handle(new UploadAttachmentCommand(s.Chat.Id, s.Member.Id, TestImages.Png()), CancellationToken.None);

        var result = await new DeleteChatHandler(s.Db, Substitute.For<IChatNotificationService>(), s.Storage)
            .Handle(new DeleteChatCommand(s.Chat.Id, s.Member.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(s.Storage.Files).IsEmpty();
    }

    [Test]
    public async Task FileSystemStorage_KeepsEachChatInItsOwnFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"nd-attachments-{Guid.NewGuid():N}");
        try
        {
            var storage = new FileSystemAttachmentStorage(root);
            var (chat, other, id) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await storage.SaveAsync(chat, id, [1, 2, 3]);
            await storage.SaveAsync(other, id, [4]);

            storage.DeleteChat(chat);

            await Assert.That(await storage.ReadAllAsync(chat, id)).IsNull();
            await Assert.That(await storage.ReadAllAsync(other, id)).IsEquivalentTo(new byte[] { 4 });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static SendMessageHandler SendHandler(NeuralDamageDbContext db, IChatNotificationService notifications) =>
        new(db, notifications, Substitute.For<IBotResponseOrchestrator>(), Substitute.For<IBotResponseQueue>(), Substitute.For<ISender>(), new ChatBotState());
}
