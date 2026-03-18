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

    [Fact]
    public async Task CreateBot_ReturnsDto()
    {
        var (db, user) = await Setup();
        using var _ = db;

        var handler = new CreateBotHandler(db);
        var result = await handler.Handle(new CreateBotCommand("GPT", "openai/gpt-4o", "Be helpful", null, 0.7, null, "gpt,chatgpt", user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("GPT", result.Value!.Name);
        Assert.Equal("openai/gpt-4o", result.Value.ModelId);
        Assert.Equal("gpt,chatgpt", result.Value.Aliases);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task UpdateBot_CreatorCanUpdate()
    {
        var (db, user) = await Setup();
        using var _ = db;
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "Old", CreatedById = user.Id };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var handler = new UpdateBotHandler(db);
        var result = await handler.Handle(new UpdateBotCommand(bot.Id, user.Id, "GPT v2", null, "New prompt", null, 0.9, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("GPT v2", bot.Name);
        Assert.Equal("New prompt", bot.SystemPrompt);
        Assert.Equal(0.9, bot.Temperature);
        Assert.Equal("openai/gpt-4o", bot.ModelId); // unchanged
    }

    [Fact]
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

        Assert.True(result.IsFailure);
        Assert.Equal("GPT", bot.Name);
    }

    [Fact]
    public async Task DeleteBot_SoftDeletes()
    {
        var (db, user) = await Setup();
        using var _ = db;
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "X", CreatedById = user.Id };
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        var handler = new DeleteBotHandler(db);
        var result = await handler.Handle(new DeleteBotCommand(bot.Id, user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(bot.IsActive);
        Assert.NotNull(await db.Bots.FirstOrDefaultAsync(b => b.Id == bot.Id)); // still in DB
    }

    [Fact]
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

        Assert.True(result.IsFailure);
        Assert.True(bot.IsActive);
    }
}
