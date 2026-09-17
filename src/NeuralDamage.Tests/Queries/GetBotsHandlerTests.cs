using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Tests.Helpers;

namespace NeuralDamage.Tests.Queries;

public class GetBotsHandlerTests
{
    [Test]
    public async Task Handle_ReturnsOnlyActiveBots()
    {
        using var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com" };
        db.Users.Add(user);
        db.Bots.Add(new Bot { Name = "Active", ModelId = "m1", SystemPrompt = "x", CreatedById = user.Id, IsActive = true });
        db.Bots.Add(new Bot { Name = "Inactive", ModelId = "m2", SystemPrompt = "x", CreatedById = user.Id, IsActive = false });
        db.Bots.Add(new Bot { Name = "Also Active", ModelId = "m3", SystemPrompt = "x", CreatedById = user.Id, IsActive = true });
        await db.SaveChangesAsync();

        var handler = new GetBotsHandler(db);
        var result = await handler.Handle(new GetBotsQuery(), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!.Count).IsEqualTo(2);
        foreach (var bot in result.Value)
        {
            await Assert.That(bot.IsActive).IsTrue();
        }
    }

    [Test]
    public async Task Handle_ReturnsAlphabetically()
    {
        using var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "test@test.com" };
        db.Users.Add(user);
        db.Bots.Add(new Bot { Name = "Zeta", ModelId = "m1", SystemPrompt = "x", CreatedById = user.Id });
        db.Bots.Add(new Bot { Name = "Alpha", ModelId = "m2", SystemPrompt = "x", CreatedById = user.Id });
        await db.SaveChangesAsync();

        var handler = new GetBotsHandler(db);
        var result = await handler.Handle(new GetBotsQuery(), CancellationToken.None);

        await Assert.That(result.Value![0].Name).IsEqualTo("Alpha");
        await Assert.That(result.Value[1].Name).IsEqualTo("Zeta");
    }
}
