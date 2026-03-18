using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;

namespace NeuralDamage.Tests.Queries;

public class GetUserChatsHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsOnlyUserChats()
    {
        using var db = TestDbContext.Create();
        var user1 = new User { ExternalId = "ext-1", Email = "user1@test.com" };
        var user2 = new User { ExternalId = "ext-2", Email = "user2@test.com" };
        db.Users.AddRange(user1, user2);

        var chat1 = new Chat { Name = "Chat 1", CreatedById = user1.Id };
        var chat2 = new Chat { Name = "Chat 2", CreatedById = user2.Id };
        var chat3 = new Chat { Name = "Chat 3", CreatedById = user1.Id };
        db.Chats.AddRange(chat1, chat2, chat3);

        db.ChatMembers.Add(new ChatMember { ChatId = chat1.Id, UserId = user1.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat2.Id, UserId = user2.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat3.Id, UserId = user1.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat2.Id, UserId = user1.Id, Role = ChatMemberRole.Member });
        await db.SaveChangesAsync();

        var handler = new GetUserChatsHandler(db);
        var result = await handler.Handle(new GetUserChatsQuery(user1.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
    }

    [Fact]
    public async Task Handle_NoChats_ReturnsEmptyList()
    {
        using var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "lonely@test.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new GetUserChatsHandler(db);
        var result = await handler.Handle(new GetUserChatsQuery(user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }
}
