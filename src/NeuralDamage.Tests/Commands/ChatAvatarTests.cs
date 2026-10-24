using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Attachments;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class ChatAvatarTests
{
    private sealed class Setup : IDisposable
    {
        public global::NeuralDamage.Infrastructure.NeuralDamageDbContext Db { get; } = TestDbContext.Create();
        public InMemoryAttachmentStorage Storage { get; } = new();
        public IChatNotificationService Notifications { get; } = Substitute.For<IChatNotificationService>();
        public User Owner { get; } = new() { ExternalId = "ext-1", Email = "owner@test.com", DisplayName = "Owner" };
        public User Member { get; } = new() { ExternalId = "ext-2", Email = "member@test.com" };
        public User Stranger { get; } = new() { ExternalId = "ext-3", Email = "stranger@test.com" };

        public SetChatAvatarHandler Set() => new(Db, Storage, new AttachmentOptions(), Notifications);

        public async Task<Chat> ChatAsync(string? directKey = null)
        {
            Db.Users.AddRange(Owner, Member, Stranger);
            var chat = new Chat { Name = "Group", CreatedById = Owner.Id, DirectKey = directKey };
            Db.Chats.Add(chat);
            Db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = Owner.Id, Role = ChatMemberRole.Owner });
            Db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = Member.Id, Role = ChatMemberRole.Member });
            await Db.SaveChangesAsync();
            return chat;
        }

        public void Dispose() => Db.Dispose();
    }

    [Test]
    public async Task TheOwner_GivesAGroupItsPicture_WhichItsMembersCanLoad()
    {
        using var s = new Setup();
        var chat = await s.ChatAsync();

        var result = await s.Set().Handle(new SetChatAvatarCommand(chat.Id, s.Owner.Id, TestImages.Png()), default);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That((await s.Db.Chats.SingleAsync()).AvatarUrl).IsEqualTo(result.Value);
        var forMember = await new GetChatAvatarHandler(s.Db, s.Storage).Handle(new GetChatAvatarQuery(chat.Id, s.Member.Id), default);
        var forStranger = await new GetChatAvatarHandler(s.Db, s.Storage).Handle(new GetChatAvatarQuery(chat.Id, s.Stranger.Id), default);
        await Assert.That(forMember.Value!.ContentType).IsEqualTo("image/png");
        await Assert.That(forStranger.IsSuccess).IsFalse();
        await s.Notifications.Received(1).NotifySystemMessage(chat.Id, "Owner changed the chat's picture.");
    }

    [Test]
    public async Task AMember_CannotChangeIt_NorCanAnythingButAnImageBeUsed()
    {
        using var s = new Setup();
        var chat = await s.ChatAsync();

        var byMember = await s.Set().Handle(new SetChatAvatarCommand(chat.Id, s.Member.Id, TestImages.Png()), default);
        var notAnImage = await s.Set().Handle(new SetChatAvatarCommand(chat.Id, s.Owner.Id, "hello"u8.ToArray()), default);

        await Assert.That(byMember.IsSuccess).IsFalse();
        await Assert.That(notAnImage.IsSuccess).IsFalse();
        await Assert.That(s.Storage.ChatAvatars).IsEmpty();
    }

    [Test]
    public async Task ADirectChat_HasNoPictureOfItsOwn()
    {
        using var s = new Setup();
        var chat = await s.ChatAsync(Chat.DirectKeyOf(Guid.NewGuid(), Guid.NewGuid()));

        var result = await s.Set().Handle(new SetChatAvatarCommand(chat.Id, s.Owner.Id, TestImages.Png()), default);

        await Assert.That(result.Error).IsEqualTo(ChatAvatars.DirectHasNone);
    }

    [Test]
    public async Task Removing_TakesThePictureAway()
    {
        using var s = new Setup();
        var chat = await s.ChatAsync();
        await s.Set().Handle(new SetChatAvatarCommand(chat.Id, s.Owner.Id, TestImages.Png()), default);

        var result = await new RemoveChatAvatarHandler(s.Db, s.Storage, s.Notifications).Handle(new RemoveChatAvatarCommand(chat.Id, s.Owner.Id), default);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That((await s.Db.Chats.SingleAsync()).AvatarUrl).IsNull();
        await Assert.That(s.Storage.ChatAvatars).IsEmpty();
    }
}
