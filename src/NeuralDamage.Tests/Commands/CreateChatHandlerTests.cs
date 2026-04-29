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
    [Fact]
    public async Task Handle_CreatesChatAndOwnerMember()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Tester" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new CreateChatHandler(db, notifications);
        var result = await handler.Handle(new CreateChatCommand("General", user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var chat = await db.Chats.FirstOrDefaultAsync();
        Assert.NotNull(chat);
        Assert.Equal("General", chat!.Name);

        var member = await db.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == chat.Id);
        Assert.NotNull(member);
        Assert.Equal(user.Id, member!.UserId);
        Assert.Equal(ChatMemberRole.Owner, member.Role);
    }

    [Fact]
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
