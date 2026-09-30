using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;

namespace NeuralDamage.Tests.Queries;

public class GetUsersHandlerTests
{
    [Test]
    public async Task Handle_ExcludesCaller()
    {
        using var db = TestDbContext.Create();
        var me = new User { ExternalId = "ext-1", Email = "me@test.com", DisplayName = "Me" };
        var other = new User { ExternalId = "ext-2", Email = "other@test.com", DisplayName = "Other" };
        db.Users.AddRange(me, other);
        await db.SaveChangesAsync();

        var result = await new GetUsersHandler(db).Handle(new GetUsersQuery(me.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!.Count).IsEqualTo(1);
        await Assert.That(result.Value[0].Id).IsEqualTo(other.Id);
    }

    [Test]
    public async Task Handle_ExcludesExistingMembersOfChat()
    {
        using var db = TestDbContext.Create();
        var me = new User { ExternalId = "ext-1", Email = "me@test.com", DisplayName = "Me" };
        var member = new User { ExternalId = "ext-2", Email = "member@test.com", DisplayName = "Member" };
        var outsider = new User { ExternalId = "ext-3", Email = "outsider@test.com", DisplayName = "Outsider" };
        db.Users.AddRange(me, member, outsider);
        var chat = new Chat { Name = "General", CreatedById = me.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = me.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = member.Id, Role = ChatMemberRole.Member });
        await db.SaveChangesAsync();

        var result = await new GetUsersHandler(db).Handle(new GetUsersQuery(me.Id, ExcludeChatId: chat.Id), CancellationToken.None);

        await Assert.That(result.Value!.Count).IsEqualTo(1);
        await Assert.That(result.Value[0].Id).IsEqualTo(outsider.Id);
    }

    [Test]
    public async Task Handle_FiltersByNameOrEmail()
    {
        using var db = TestDbContext.Create();
        var me = new User { ExternalId = "ext-1", Email = "me@test.com", DisplayName = "Me" };
        db.Users.AddRange(
            me,
            new User { ExternalId = "ext-2", Email = "alice@test.com", DisplayName = "Alice" },
            new User { ExternalId = "ext-3", Email = "bob@example.org", DisplayName = "Bob" },
            new User { ExternalId = "ext-4", Email = "carol@test.com", DisplayName = "Carol" });
        await db.SaveChangesAsync();

        var byName = await new GetUsersHandler(db).Handle(new GetUsersQuery(me.Id, Search: "ALI"), CancellationToken.None);
        var byEmail = await new GetUsersHandler(db).Handle(new GetUsersQuery(me.Id, Search: "example"), CancellationToken.None);

        await Assert.That(byName.Value!.Count).IsEqualTo(1);
        await Assert.That(byName.Value[0].DisplayName).IsEqualTo("Alice");
        await Assert.That(byEmail.Value!.Count).IsEqualTo(1);
        await Assert.That(byEmail.Value[0].DisplayName).IsEqualTo("Bob");
    }
}
