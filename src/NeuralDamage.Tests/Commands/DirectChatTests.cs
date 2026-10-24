using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class DirectChatTests
{
    [Test]
    public async Task MessagingABotTwice_LandsInTheSameChat()
    {
        using var db = TestDbContext.Create();
        var me = new User { ExternalId = "ext-1", Email = "me@test.com", DisplayName = "Me" };
        db.Users.Add(me);
        var bot = new Bot { Name = "Rex", ModelId = "m", CreatedById = me.Id };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();
        var notifications = Substitute.For<IChatNotificationService>();
        var handler = new OpenDirectChatHandler(db, notifications);

        var first = await handler.Handle(new OpenDirectChatCommand(me.Id, bot.Id, null), default);
        var second = await handler.Handle(new OpenDirectChatCommand(me.Id, bot.Id, null), default);

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(first.Value!.IsDirect).IsTrue();
        await Assert.That(first.Value.Name).IsEqualTo("Rex");
        await Assert.That(second.Value!.Id).IsEqualTo(first.Value.Id);
        await Assert.That(await db.Chats.CountAsync()).IsEqualTo(1);
        await Assert.That(await db.ChatMembers.CountAsync()).IsEqualTo(2);
        await notifications.Received(1).NotifyUserChatCreated(me.Id, Arg.Any<ChatDto>());
    }

    [Test]
    public async Task TheKey_IsTheSameWhicheverSideOpensIt()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await Assert.That(Chat.DirectKeyOf(a, b)).IsEqualTo(Chat.DirectKeyOf(b, a));
    }

    [Test]
    public async Task APersonCanBeMessaged_ButNotYourself()
    {
        using var db = TestDbContext.Create();
        var me = new User { ExternalId = "ext-1", Email = "me@test.com", DisplayName = "Me" };
        var friend = new User { ExternalId = "ext-2", Email = "friend@test.com", DisplayName = "Friend" };
        db.Users.AddRange(me, friend);
        await db.SaveChangesAsync();
        var notifications = Substitute.For<IChatNotificationService>();
        var handler = new OpenDirectChatHandler(db, notifications);

        var mine = await handler.Handle(new OpenDirectChatCommand(me.Id, null, friend.Id), default);
        var theirs = await handler.Handle(new OpenDirectChatCommand(friend.Id, null, me.Id), default);
        var self = await handler.Handle(new OpenDirectChatCommand(me.Id, null, me.Id), default);

        await Assert.That(theirs.Value!.Id).IsEqualTo(mine.Value!.Id);
        await Assert.That(self.IsSuccess).IsFalse();
        await notifications.Received(1).NotifyUserChatCreated(friend.Id, Arg.Any<ChatDto>());
    }

    [Test]
    public async Task APrivateBot_CannotBeMessaged()
    {
        using var db = TestDbContext.Create();
        var me = new User { ExternalId = "ext-1", Email = "me@test.com" };
        db.Users.Add(me);
        var bot = new Bot { Name = "Secret", ModelId = "m", CreatedById = me.Id, IsPublic = false };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var result = await new OpenDirectChatHandler(db, Substitute.For<IChatNotificationService>())
            .Handle(new OpenDirectChatCommand(me.Id, bot.Id, null), default);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task ADirectChat_KeepsItsPair_AndItsName()
    {
        using var db = TestDbContext.Create();
        var me = new User { ExternalId = "ext-1", Email = "me@test.com" };
        var friend = new User { ExternalId = "ext-2", Email = "friend@test.com" };
        db.Users.AddRange(me, friend);
        var bot = new Bot { Name = "Rex", ModelId = "m", CreatedById = me.Id };
        db.Bots.Add(bot);
        var chat = new Chat { Name = "Rex", CreatedById = me.Id, DirectKey = Chat.DirectKeyOf(me.Id, bot.Id) };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = me.Id, Role = ChatMemberRole.Owner });
        var botMember = new ChatMember { ChatId = chat.Id, BotId = bot.Id };
        db.ChatMembers.Add(botMember);
        await db.SaveChangesAsync();
        var notifications = Substitute.For<IChatNotificationService>();

        var added = await new AddMemberHandler(db, notifications, new ChatBotState(), Substitute.For<IOpenRouterService>(), new ModelPolicy(0, 0))
            .Handle(new AddMemberCommand(chat.Id, friend.Id, null, me.Id), default);
        var removed = await new RemoveMemberHandler(db, notifications)
            .Handle(new RemoveMemberCommand(chat.Id, botMember.Id, me.Id), default);
        var renamed = await new UpdateChatHandler(db, notifications)
            .Handle(new UpdateChatCommand(chat.Id, "Something else", me.Id), default);

        await Assert.That(added.Error).IsEqualTo(DirectChatRules.KeepsItsPair);
        await Assert.That(removed.Error).IsEqualTo(DirectChatRules.KeepsItsPair);
        await Assert.That(renamed.IsSuccess).IsFalse();
        await Assert.That(await db.ChatMembers.CountAsync()).IsEqualTo(2);
        await Assert.That((await db.Chats.SingleAsync()).Name).IsEqualTo("Rex");
    }
}
