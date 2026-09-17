using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Tests.Helpers;

namespace NeuralDamage.Tests.Commands;

public class BotCrudHandlerTests
{
    private static async Task<(NeuralDamage.Infrastructure.NeuralDamageDbContext db, User user)> Setup()
    {
        var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "creator@test.com", DisplayName = "Creator" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (db, user);
    }

    [Test]
    public async Task CreateBot_ReturnsDto()
    {
        var (db, user) = await Setup();
        using var _ = db;

        var handler = new CreateBotHandler(db);
        var result = await handler.Handle(new CreateBotCommand("GPT", "openai/gpt-4o", "Be helpful", null, 0.7, null, "gpt,chatgpt", user.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!.Name).IsEqualTo("GPT");
        await Assert.That(result.Value.ModelId).IsEqualTo("openai/gpt-4o");
        await Assert.That(result.Value.Aliases).IsEqualTo("gpt,chatgpt");
        await Assert.That(result.Value.IsActive).IsTrue();
    }

    [Test]
    public async Task UpdateBot_CreatorCanUpdate()
    {
        var (db, user) = await Setup();
        using var _ = db;
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "Old", CreatedById = user.Id };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var handler = new UpdateBotHandler(db);
        var result = await handler.Handle(new UpdateBotCommand(bot.Id, user.Id, "GPT v2", null, "New prompt", null, 0.9, null, null, null), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(bot.Name).IsEqualTo("GPT v2");
        await Assert.That(bot.SystemPrompt).IsEqualTo("New prompt");
        await Assert.That(bot.Temperature).IsEqualTo(0.9);
        await Assert.That(bot.ModelId).IsEqualTo("openai/gpt-4o"); // unchanged
    }

    [Test]
    public async Task UpdateBot_NonCreator_Fails()
    {
        var (db, user) = await Setup();
        using var _ = db;
        var other = new User { ExternalId = "ext-2", Email = "other@test.com" };
        db.Users.Add(other);
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "X", CreatedById = user.Id };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var handler = new UpdateBotHandler(db);
        var result = await handler.Handle(new UpdateBotCommand(bot.Id, other.Id, "Hacked", null, null, null, null, null, null, null), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(bot.Name).IsEqualTo("GPT");
    }

    [Test]
    public async Task DeleteBot_SoftDeletes()
    {
        var (db, user) = await Setup();
        using var _ = db;
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "X", CreatedById = user.Id };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var handler = new DeleteBotHandler(db);
        var result = await handler.Handle(new DeleteBotCommand(bot.Id, user.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(bot.IsActive).IsFalse();
        await Assert.That(await db.Bots.FirstOrDefaultAsync(b => b.Id == bot.Id)).IsNotNull(); // still in DB
    }

    [Test]
    public async Task DeleteBot_NonCreator_Fails()
    {
        var (db, user) = await Setup();
        using var _ = db;
        var other = new User { ExternalId = "ext-2", Email = "other@test.com" };
        db.Users.Add(other);
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "X", CreatedById = user.Id };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var handler = new DeleteBotHandler(db);
        var result = await handler.Handle(new DeleteBotCommand(bot.Id, other.Id), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(bot.IsActive).IsTrue();
    }
}
