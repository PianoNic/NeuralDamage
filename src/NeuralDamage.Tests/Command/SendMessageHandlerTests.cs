using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Command;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class SendMessageHandlerTests
{
    private static IChatNotificationService MockNotifications() => Substitute.For<IChatNotificationService>();
    private static IBotResponseOrchestrator MockOrchestrator() => Substitute.For<IBotResponseOrchestrator>();
    private static IBotResponseQueue MockQueue() => Substitute.For<IBotResponseQueue>();

    private static async Task<(NeuralDamage.Infrastructure.NeuralDamageDbContext db, User user, Chat chat)> SetupChatWithMember()
    {
        var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Tester" };
        db.Users.Add(user);
        var chat = new Chat { Name = "General", CreatedById = user.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });
        await db.SaveChangesAsync();
        return (db, user, chat);
    }

    [Fact]
    public async Task Handle_SendsMessage()
    {
        var (db, user, chat) = await SetupChatWithMember();
        using var _ = db;
        var notifications = MockNotifications();

        var handler = new SendMessageHandler(db, notifications, MockOrchestrator(), MockQueue());
        var result = await handler.Handle(new SendMessageCommand(chat.Id, user.Id, "Hello world"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var msg = await db.Messages.FirstOrDefaultAsync();
        Assert.NotNull(msg);
        Assert.Equal("Hello world", msg!.Content);
        Assert.Equal(user.Id, msg.SenderUserId);
        await notifications.Received(1).NotifyMessageNew(chat.Id, Arg.Any<NeuralDamage.Application.Dtos.MessageDto>());
    }

    [Fact]
    public async Task Handle_WithReply_SetsReplyToId()
    {
        var (db, user, chat) = await SetupChatWithMember();
        using var _ = db;
        var original = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "Original" };
        db.Messages.Add(original);
        await db.SaveChangesAsync();

        var handler = new SendMessageHandler(db, MockNotifications(), MockOrchestrator(), MockQueue());
        var result = await handler.Handle(new SendMessageCommand(chat.Id, user.Id, "Reply", original.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var reply = await db.Messages.FirstOrDefaultAsync(m => m.Content == "Reply");
        Assert.NotNull(reply);
        Assert.Equal(original.Id, reply!.ReplyToId);
    }

    [Fact]
    public async Task Handle_NonMember_ReturnsFailure()
    {
        var (db, user, chat) = await SetupChatWithMember();
        using var _ = db;
        var outsider = new User { ExternalId = "ext-2", Email = "outsider@test.com" };
        db.Users.Add(outsider);
        await db.SaveChangesAsync();

        var handler = new SendMessageHandler(db, MockNotifications(), MockOrchestrator(), MockQueue());
        var result = await handler.Handle(new SendMessageCommand(chat.Id, outsider.Id, "Sneaky"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("not a member", result.Error!);
    }

    [Fact]
    public async Task Handle_InvalidReplyTarget_ReturnsFailure()
    {
        var (db, user, chat) = await SetupChatWithMember();
        using var _ = db;

        var handler = new SendMessageHandler(db, MockNotifications(), MockOrchestrator(), MockQueue());
        var result = await handler.Handle(new SendMessageCommand(chat.Id, user.Id, "Reply", Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Reply target", result.Error!);
    }
}
