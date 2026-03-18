using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;

namespace NeuralDamage.Tests.Commands;

public class CreateChatHandlerTests
{
    [Fact]
    public async Task Handle_CreatesChatAndOwnerMember()
    {
        using var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com", DisplayName = "Tester" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new CreateChatHandler(db);
        var result = await handler.Handle(new CreateChatCommand("General", user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("General", result.Value!.Name);

        var chat = await db.Chats.FirstOrDefaultAsync();
        Assert.NotNull(chat);

        var member = await db.ChatMembers.FirstOrDefaultAsync(cm => cm.ChatId == chat!.Id);
        Assert.NotNull(member);
        Assert.Equal(user.Id, member!.UserId);
        Assert.Equal(ChatMemberRole.Owner, member.Role);
    }

    [Fact]
    public async Task Handle_ReturnsDto_WithCorrectFields()
    {
        using var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new CreateChatHandler(db);
        var result = await handler.Handle(new CreateChatCommand("My Chat", user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("My Chat", result.Value!.Name);
        Assert.Equal(user.Id, result.Value.CreatedById);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }
}
