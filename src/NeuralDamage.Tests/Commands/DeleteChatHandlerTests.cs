using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class DeleteChatHandlerTests
{
    [Fact]
    public async Task Handle_OwnerCanDelete()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com" };
        db.Users.Add(user);
        var chat = new Chat { Name = "Doomed", CreatedById = user.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });
        await db.SaveChangesAsync();

        var handler = new DeleteChatHandler(db, notifications);
        var result = await handler.Handle(new DeleteChatCommand(chat.Id, user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await db.Chats.FirstOrDefaultAsync(c => c.Id == chat.Id));
        await notifications.Received(1).NotifyChatDeleted(chat.Id);
    }

    [Fact]
    public async Task Handle_NonOwnerCannotDelete()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        var other = new User { ExternalId = "ext-2", Email = "other@test.com" };
        db.Users.AddRange(owner, other);
        var chat = new Chat { Name = "Protected", CreatedById = owner.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = other.Id, Role = ChatMemberRole.Member });
        await db.SaveChangesAsync();

        var handler = new DeleteChatHandler(db, notifications);
        var result = await handler.Handle(new DeleteChatCommand(chat.Id, other.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.NotNull(await db.Chats.FirstOrDefaultAsync(c => c.Id == chat.Id));
    }
}
