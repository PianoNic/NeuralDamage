using NeuralDamage.Application.Command;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class UpdateChatHandlerTests
{
    [Fact]
    public async Task Handle_OwnerCanUpdateName()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com" };
        db.Users.Add(user);
        var chat = new Chat { Name = "Old Name", CreatedById = user.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });
        await db.SaveChangesAsync();

        var handler = new UpdateChatHandler(db, notifications);
        var result = await handler.Handle(new UpdateChatCommand(chat.Id, "New Name", user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", chat.Name);
        await notifications.Received(1).NotifyChatUpdated(chat.Id, Arg.Any<NeuralDamage.Application.Dtos.ChatDto>());
    }

    [Fact]
    public async Task Handle_NonOwnerCannotUpdate()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        var other = new User { ExternalId = "ext-2", Email = "other@test.com" };
        db.Users.AddRange(owner, other);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = other.Id, Role = ChatMemberRole.Member });
        await db.SaveChangesAsync();

        var handler = new UpdateChatHandler(db, notifications);
        var result = await handler.Handle(new UpdateChatCommand(chat.Id, "Hacked", other.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Chat", chat.Name);
    }

    [Fact]
    public async Task Handle_ChatNotFound_ReturnsFailure()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();

        var handler = new UpdateChatHandler(db, notifications);
        var result = await handler.Handle(new UpdateChatCommand(Guid.NewGuid(), "Name", Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error!);
    }
}
