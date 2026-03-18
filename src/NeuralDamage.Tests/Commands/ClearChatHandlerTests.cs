using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class ClearChatHandlerTests
{
    [Fact]
    public async Task Handle_OwnerCanClear()
    {
        using var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com" };
        db.Users.Add(user);
        var chat = new Chat { Name = "Chat", CreatedById = user.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });
        db.Messages.Add(new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "msg 1" });
        db.Messages.Add(new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "msg 2" });
        db.Messages.Add(new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "msg 3" });
        await db.SaveChangesAsync();

        var handler = new ClearChatHandler(db, Substitute.For<IChatNotificationService>());
        var result = await handler.Handle(new ClearChatCommand(chat.Id, user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, await db.Messages.CountAsync(m => m.ChatId == chat.Id));
    }

    [Fact]
    public async Task Handle_NonOwnerCannotClear()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        var member = new User { ExternalId = "ext-2", Email = "member@test.com" };
        db.Users.AddRange(owner, member);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = member.Id, Role = ChatMemberRole.Member });
        db.Messages.Add(new Message { ChatId = chat.Id, SenderUserId = owner.Id, Content = "msg" });
        await db.SaveChangesAsync();

        var handler = new ClearChatHandler(db, Substitute.For<IChatNotificationService>());
        var result = await handler.Handle(new ClearChatCommand(chat.Id, member.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, await db.Messages.CountAsync(m => m.ChatId == chat.Id));
    }
}
