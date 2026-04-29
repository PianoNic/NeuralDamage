using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class AddMemberHandlerTests
{
    private static IChatNotificationService MockNotifications() => Substitute.For<IChatNotificationService>();

    [Fact]
    public async Task Handle_AddsUserMember()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        var newUser = new User { ExternalId = "ext-2", Email = "new@test.com", DisplayName = "New User" };
        db.Users.AddRange(owner, newUser);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        await db.SaveChangesAsync();

        var handler = new AddMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new AddMemberCommand(chat.Id, newUser.Id, null, owner.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await db.ChatMembers.CountAsync(cm => cm.ChatId == chat.Id));
    }

    [Fact]
    public async Task Handle_AddsBotMember()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        db.Users.Add(owner);
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", CreatedById = owner.Id };
        db.Bots.Add(bot);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        await db.SaveChangesAsync();

        var handler = new AddMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new AddMemberCommand(chat.Id, null, bot.Id, owner.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var botMember = await db.ChatMembers.FirstOrDefaultAsync(cm => cm.BotId == bot.Id);
        Assert.NotNull(botMember);
    }

    [Fact]
    public async Task Handle_NonOwnerCannotAdd()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        var member = new User { ExternalId = "ext-2", Email = "member@test.com" };
        var newUser = new User { ExternalId = "ext-3", Email = "new@test.com" };
        db.Users.AddRange(owner, member, newUser);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = member.Id, Role = ChatMemberRole.Member });
        await db.SaveChangesAsync();

        var handler = new AddMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new AddMemberCommand(chat.Id, newUser.Id, null, member.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Handle_DuplicateUser_ReturnsFailure()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        db.Users.Add(owner);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        await db.SaveChangesAsync();

        var handler = new AddMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new AddMemberCommand(chat.Id, owner.Id, null, owner.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("already", result.Error!);
    }

    [Fact]
    public async Task Handle_BothUserAndBot_ReturnsFailure()
    {
        using var db = TestDbContext.Create();

        var handler = new AddMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new AddMemberCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Only one", result.Error!);
    }

    [Fact]
    public async Task Handle_NeitherUserNorBot_ReturnsFailure()
    {
        using var db = TestDbContext.Create();

        var handler = new AddMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new AddMemberCommand(Guid.NewGuid(), null, null, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Either", result.Error!);
    }
}
