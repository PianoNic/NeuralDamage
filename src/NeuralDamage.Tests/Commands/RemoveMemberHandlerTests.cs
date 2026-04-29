using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class RemoveMemberHandlerTests
{
    private static IChatNotificationService MockNotifications() => Substitute.For<IChatNotificationService>();

    [Fact]
    public async Task Handle_OwnerCanRemoveMember()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        var member = new User { ExternalId = "ext-2", Email = "member@test.com" };
        db.Users.AddRange(owner, member);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        var ownerMember = new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner };
        var userMember = new ChatMember { ChatId = chat.Id, UserId = member.Id, Role = ChatMemberRole.Member };
        db.ChatMembers.AddRange(ownerMember, userMember);
        await db.SaveChangesAsync();

        var handler = new RemoveMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new RemoveMemberCommand(chat.Id, userMember.Id, owner.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await db.ChatMembers.CountAsync(cm => cm.ChatId == chat.Id));
    }

    [Fact]
    public async Task Handle_MemberCanRemoveSelf()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        var member = new User { ExternalId = "ext-2", Email = "member@test.com" };
        db.Users.AddRange(owner, member);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        var userMember = new ChatMember { ChatId = chat.Id, UserId = member.Id, Role = ChatMemberRole.Member };
        db.ChatMembers.Add(userMember);
        await db.SaveChangesAsync();

        var handler = new RemoveMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new RemoveMemberCommand(chat.Id, userMember.Id, member.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_CannotRemoveOwner()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        db.Users.Add(owner);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        var ownerMember = new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner };
        db.ChatMembers.Add(ownerMember);
        await db.SaveChangesAsync();

        var handler = new RemoveMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new RemoveMemberCommand(chat.Id, ownerMember.Id, owner.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("owner", result.Error!);
    }

    [Fact]
    public async Task Handle_RegularMemberCannotRemoveOthers()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        var member1 = new User { ExternalId = "ext-2", Email = "m1@test.com" };
        var member2 = new User { ExternalId = "ext-3", Email = "m2@test.com" };
        db.Users.AddRange(owner, member1, member2);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = member1.Id, Role = ChatMemberRole.Member });
        var m2Member = new ChatMember { ChatId = chat.Id, UserId = member2.Id, Role = ChatMemberRole.Member };
        db.ChatMembers.Add(m2Member);
        await db.SaveChangesAsync();

        var handler = new RemoveMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new RemoveMemberCommand(chat.Id, m2Member.Id, member1.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Handle_MemberNotFound_ReturnsFailure()
    {
        using var db = TestDbContext.Create();

        var handler = new RemoveMemberHandler(db, MockNotifications());
        var result = await handler.Handle(new RemoveMemberCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error!);
    }
}
