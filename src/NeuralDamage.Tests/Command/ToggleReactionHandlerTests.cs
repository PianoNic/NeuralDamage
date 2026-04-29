using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Command;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class ToggleReactionHandlerTests
{
    private static async Task<(NeuralDamage.Infrastructure.NeuralDamageDbContext db, User user, Chat chat, Message message)> Setup()
    {
        var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com" };
        db.Users.Add(user);
        var chat = new Chat { Name = "Chat", CreatedById = user.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });
        var msg = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "Hello" };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();
        return (db, user, chat, msg);
    }

    [Fact]
    public async Task Handle_AddsReaction()
    {
        var (db, user, chat, msg) = await Setup();
        using var _ = db;
        var notifications = Substitute.For<IChatNotificationService>();

        var handler = new ToggleReactionHandler(db, notifications);
        var result = await handler.Handle(new ToggleReactionCommand(chat.Id, msg.Id, "👍", user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await db.Reactions.CountAsync());
    }

    [Fact]
    public async Task Handle_TogglesOff_WhenAlreadyExists()
    {
        var (db, user, chat, msg) = await Setup();
        using var _ = db;
        db.Reactions.Add(new Reaction { MessageId = msg.Id, UserId = user.Id, Emoji = "👍" });
        await db.SaveChangesAsync();
        var notifications = Substitute.For<IChatNotificationService>();

        var handler = new ToggleReactionHandler(db, notifications);
        var result = await handler.Handle(new ToggleReactionCommand(chat.Id, msg.Id, "👍", user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, await db.Reactions.CountAsync());
    }

    [Fact]
    public async Task Handle_DifferentEmoji_AddsBoth()
    {
        var (db, user, chat, msg) = await Setup();
        using var _ = db;
        db.Reactions.Add(new Reaction { MessageId = msg.Id, UserId = user.Id, Emoji = "👍" });
        await db.SaveChangesAsync();
        var notifications = Substitute.For<IChatNotificationService>();

        var handler = new ToggleReactionHandler(db, notifications);
        var result = await handler.Handle(new ToggleReactionCommand(chat.Id, msg.Id, "🔥", user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await db.Reactions.CountAsync());
    }

    [Fact]
    public async Task Handle_MessageNotFound_ReturnsFailure()
    {
        var (db, user, chat, _) = await Setup();
        using var _ = db;
        var notifications = Substitute.For<IChatNotificationService>();

        var handler = new ToggleReactionHandler(db, notifications);
        var result = await handler.Handle(new ToggleReactionCommand(chat.Id, Guid.NewGuid(), "👍", user.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Handle_BroadcastsReactionUpdated()
    {
        var (db, user, chat, msg) = await Setup();
        using var _ = db;
        var notifications = Substitute.For<IChatNotificationService>();

        var handler = new ToggleReactionHandler(db, notifications);
        await handler.Handle(new ToggleReactionCommand(chat.Id, msg.Id, "❤️", user.Id), CancellationToken.None);

        await notifications.Received(1).NotifyReactionUpdated(chat.Id, msg.Id, Arg.Any<List<NeuralDamage.Application.Dtos.ReactionDto>>());
    }
}
