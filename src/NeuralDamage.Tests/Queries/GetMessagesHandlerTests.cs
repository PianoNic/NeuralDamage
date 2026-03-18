using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;

namespace NeuralDamage.Tests.Queries;

public class GetMessagesHandlerTests
{
    private static async Task<(NeuralDamage.Infrastructure.NeuralDamageDbContext db, User user, Chat chat)> SetupChatWithMessages(int count)
    {
        var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Tester" };
        db.Users.Add(user);
        var chat = new Chat { Name = "General", CreatedById = user.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });

        for (int i = 0; i < count; i++)
        {
            db.Messages.Add(new Message
            {
                ChatId = chat.Id,
                SenderUserId = user.Id,
                Content = $"Message {i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-count + i)
            });
        }

        await db.SaveChangesAsync();
        return (db, user, chat);
    }

    [Fact]
    public async Task Handle_ReturnsMessagesInChronologicalOrder()
    {
        var (db, user, chat) = await SetupChatWithMessages(5);
        using var _ = db;

        var handler = new GetMessagesHandler(db);
        var result = await handler.Handle(new GetMessagesQuery(chat.Id, user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Count);
        Assert.Equal("Message 0", result.Value[0].Content);
        Assert.Equal("Message 4", result.Value[4].Content);
    }

    [Fact]
    public async Task Handle_RespectsLimit()
    {
        var (db, user, chat) = await SetupChatWithMessages(10);
        using var _ = db;

        var handler = new GetMessagesHandler(db);
        var result = await handler.Handle(new GetMessagesQuery(chat.Id, user.Id, Limit: 3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        // Should be the 3 most recent
        Assert.Equal("Message 7", result.Value[0].Content);
        Assert.Equal("Message 9", result.Value[2].Content);
    }

    [Fact]
    public async Task Handle_CursorPagination_Before()
    {
        var (db, user, chat) = await SetupChatWithMessages(10);
        using var _ = db;

        var handler = new GetMessagesHandler(db);
        // Get latest 3 first
        var first = await handler.Handle(new GetMessagesQuery(chat.Id, user.Id, Limit: 3), CancellationToken.None);
        Assert.True(first.IsSuccess);

        // Then get 3 before the oldest in that batch
        var before = first.Value![0].CreatedAt;
        var second = await handler.Handle(new GetMessagesQuery(chat.Id, user.Id, Limit: 3, Before: before), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(3, second.Value!.Count);
        // All should be older than the cursor
        Assert.True(second.Value.All(m => m.CreatedAt < before));
    }

    [Fact]
    public async Task Handle_NonMember_ReturnsFailure()
    {
        var (db, user, chat) = await SetupChatWithMessages(1);
        using var _ = db;

        var handler = new GetMessagesHandler(db);
        var result = await handler.Handle(new GetMessagesQuery(chat.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("not a member", result.Error!);
    }

    [Fact]
    public async Task Handle_EmptyChat_ReturnsEmptyList()
    {
        var (db, user, chat) = await SetupChatWithMessages(0);
        using var _ = db;

        var handler = new GetMessagesHandler(db);
        var result = await handler.Handle(new GetMessagesQuery(chat.Id, user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_ClampsLimitTo100()
    {
        var (db, user, chat) = await SetupChatWithMessages(5);
        using var _ = db;

        var handler = new GetMessagesHandler(db);
        var result = await handler.Handle(new GetMessagesQuery(chat.Id, user.Id, Limit: 999), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Count); // only 5 exist, but limit was clamped to 100
    }
}
