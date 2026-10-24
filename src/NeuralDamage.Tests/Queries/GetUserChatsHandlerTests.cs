using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;

namespace NeuralDamage.Tests.Queries;

public class GetUserChatsHandlerTests
{
    [Test]
    public async Task Handle_ReturnsOnlyUserChats()
    {
        using var db = TestDbContext.Create();
        var user1 = new User { ExternalId = "ext-1", Email = "user1@test.com" };
        var user2 = new User { ExternalId = "ext-2", Email = "user2@test.com" };
        db.Users.AddRange(user1, user2);

        var chat1 = new Chat { Name = "Chat 1", CreatedById = user1.Id };
        var chat2 = new Chat { Name = "Chat 2", CreatedById = user2.Id };
        var chat3 = new Chat { Name = "Chat 3", CreatedById = user1.Id };
        db.Chats.AddRange(chat1, chat2, chat3);

        db.ChatMembers.Add(new ChatMember { ChatId = chat1.Id, UserId = user1.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat2.Id, UserId = user2.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat3.Id, UserId = user1.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat2.Id, UserId = user1.Id, Role = ChatMemberRole.Member });
        await db.SaveChangesAsync();

        var handler = new GetUserChatsHandler(db);
        var result = await handler.Handle(new GetUserChatsQuery(user1.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Handle_PreviewsTheLastMessageAndTheOtherMembersBotsFirst()
    {
        using var db = TestDbContext.Create();
        var me = new User { ExternalId = "ext-1", Email = "me@test.com", DisplayName = "Me" };
        var friend = new User { ExternalId = "ext-2", Email = "friend@test.com", DisplayName = "Friend" };
        db.Users.AddRange(me, friend);
        var bot = new Bot { Name = "Rex", ModelId = "m", CreatedById = me.Id };
        db.Bots.Add(bot);
        var chat = new Chat { Name = "Group", CreatedById = me.Id };
        db.Chats.Add(chat);
        var start = DateTime.UtcNow.AddMinutes(-10);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = me.Id, Role = ChatMemberRole.Owner, JoinedAt = start });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = friend.Id, Role = ChatMemberRole.Member, JoinedAt = start.AddMinutes(1) });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, BotId = bot.Id, Role = ChatMemberRole.Member, JoinedAt = start.AddMinutes(2) });
        db.Messages.Add(new Message { ChatId = chat.Id, SenderUserId = me.Id, Content = "first", CreatedAt = start.AddMinutes(3) });
        db.Messages.Add(new Message { ChatId = chat.Id, SenderBotId = bot.Id, Content = "on my\nway", CreatedAt = start.AddMinutes(4) });
        await db.SaveChangesAsync();

        var result = await new GetUserChatsHandler(db).Handle(new GetUserChatsQuery(me.Id), CancellationToken.None);

        var preview = result.Value!.Single().Preview!;
        await Assert.That(preview.LastMessage).IsEqualTo("on my way");
        await Assert.That(preview.LastSenderName).IsEqualTo("Rex");
        await Assert.That(preview.MemberCount).IsEqualTo(3);
        await Assert.That(preview.Members.Select(m => m.Name)).IsEquivalentTo(["Rex", "Friend"]);
        await Assert.That(preview.Members[0].IsBot).IsTrue();
    }

    [Test]
    public async Task Handle_OwnLastMessageIsFromYou_AndAStickerSaysSo()
    {
        using var db = TestDbContext.Create();
        var me = new User { ExternalId = "ext-1", Email = "me@test.com", DisplayName = "Me" };
        db.Users.Add(me);
        var chat = new Chat { Name = "Solo", CreatedById = me.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = me.Id, Role = ChatMemberRole.Owner });
        db.Messages.Add(new Message { ChatId = chat.Id, SenderUserId = me.Id, Content = "", StickerId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        var result = await new GetUserChatsHandler(db).Handle(new GetUserChatsQuery(me.Id), CancellationToken.None);

        var preview = result.Value!.Single().Preview!;
        await Assert.That(preview.LastMessage).IsEqualTo("Sent a sticker");
        await Assert.That(preview.LastSenderName).IsEqualTo("You");
        await Assert.That(preview.Members).IsEmpty();
    }

    [Test]
    public async Task Handle_NoChats_ReturnsEmptyList()
    {
        using var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "lonely@test.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new GetUserChatsHandler(db);
        var result = await handler.Handle(new GetUserChatsQuery(user.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!).IsEmpty();
    }
}
