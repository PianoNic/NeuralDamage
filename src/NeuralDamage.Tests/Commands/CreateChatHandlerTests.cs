using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class CreateChatHandlerTests
{
    [Test]
    public async Task Handle_CreatesChatAndOwnerMember()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Tester" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new CreateChatHandler(db, notifications);
        var result = await handler.Handle(new CreateChatCommand("General", user.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();

        var chat = await db.Chats.FirstOrDefaultAsync();
        await Assert.That(chat).IsNotNull();
        await Assert.That(chat!.Name).IsEqualTo("General");

        var member = await db.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == chat.Id);
        await Assert.That(member).IsNotNull();
        await Assert.That(member!.UserId).IsEqualTo(user.Id);
        await Assert.That(member.Role).IsEqualTo(ChatMemberRole.Owner);
    }

    [Test]
    public async Task Handle_NotifiesUserViaChatCreated()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new CreateChatHandler(db, notifications);
        await handler.Handle(new CreateChatCommand("My Chat", user.Id), CancellationToken.None);

        await notifications.Received(1).NotifyUserChatCreated(user.Id, Arg.Any<NeuralDamage.Infrastructure.Dtos.ChatDto>());
    }
}
