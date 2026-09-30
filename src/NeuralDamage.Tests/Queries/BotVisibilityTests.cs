using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Queries;

/// <summary>Public bots are listed for everyone; a private bot lives in its one chat.</summary>
public class BotVisibilityTests
{
    private static readonly ModelPolicy NoCap = new(0, 0);

    private sealed record World(NeuralDamageDbContext Db, User Owner, User Stranger, Chat Chat, Chat OtherChat, Bot Public, Bot Private);

    private static async Task<World> SeedAsync()
    {
        var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com", DisplayName = "Alice" };
        var stranger = new User { ExternalId = "ext-2", Email = "stranger@test.com", DisplayName = "Bob" };
        var chat = new Chat { Name = "Home", CreatedById = owner.Id };
        var otherChat = new Chat { Name = "Elsewhere", CreatedById = owner.Id };
        var publicBot = new Bot { Name = "Public", ModelId = "m/public", SystemPrompt = "x", CreatedById = owner.Id };
        var privateBot = new Bot { Name = "Private", ModelId = "m/private", SystemPrompt = "x", CreatedById = owner.Id, IsPublic = false, ChatId = chat.Id };
        db.AddRange(owner, stranger, chat, otherChat, publicBot, privateBot);
        db.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner },
            new ChatMember { ChatId = otherChat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner },
            new ChatMember { ChatId = chat.Id, BotId = publicBot.Id },
            new ChatMember { ChatId = chat.Id, BotId = privateBot.Id });
        await db.SaveChangesAsync();
        return new World(db, owner, stranger, chat, otherChat, publicBot, privateBot);
    }

    private static GetBotsHandler BotsHandler(NeuralDamageDbContext db) => new(db, Substitute.For<IOpenRouterService>(), NoCap);

    [Test]
    public async Task List_ExcludesPrivateBots()
    {
        var w = await SeedAsync();
        using var _ = w.Db;

        var result = await BotsHandler(w.Db).Handle(new GetBotsQuery(), CancellationToken.None);

        await Assert.That(result.Value!.Select(b => b.Name)).IsEquivalentTo(["Public"]);
    }

    [Test]
    public async Task List_Mine_OnlyTheCallersPublicBots()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        w.Db.Bots.Add(new Bot { Name = "Bobs", ModelId = "m", SystemPrompt = "x", CreatedById = w.Stranger.Id });
        await w.Db.SaveChangesAsync();

        var mine = await BotsHandler(w.Db).Handle(new GetBotsQuery(w.Stranger.Id), CancellationToken.None);

        await Assert.That(mine.Value!.Select(b => b.Name)).IsEquivalentTo(["Bobs"]);
    }

    [Test]
    public async Task List_CarriesCreatorAndUsageCounts()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        w.Db.ChatMembers.Add(new ChatMember { ChatId = w.OtherChat.Id, BotId = w.Public.Id });
        var now = DateTime.UtcNow;
        w.Db.Messages.AddRange(
            new Message { ChatId = w.Chat.Id, SenderBotId = w.Public.Id, Content = "now", CreatedAt = now },
            new Message { ChatId = w.Chat.Id, SenderBotId = w.Public.Id, Content = "3 days ago", CreatedAt = now.AddDays(-3) },
            new Message { ChatId = w.Chat.Id, SenderBotId = w.Public.Id, Content = "last month", CreatedAt = now.AddDays(-30) },
            new Message { ChatId = w.Chat.Id, SenderUserId = w.Owner.Id, Content = "a person", CreatedAt = now });
        await w.Db.SaveChangesAsync();

        var bot = (await BotsHandler(w.Db).Handle(new GetBotsQuery(), CancellationToken.None)).Value!.Single();

        await Assert.That(bot.CreatedBy).IsEqualTo(new BotCreatorDto(w.Owner.Id, "Alice"));
        await Assert.That(bot.IsPublic).IsTrue();
        await Assert.That(bot.ChatId).IsNull();
        await Assert.That(bot.ChatCount).IsEqualTo(2);
        await Assert.That(bot.RepliesToday).IsEqualTo(1);
        await Assert.That(bot.RepliesLast7Days).IsEqualTo(2);
        await Assert.That(bot.ModelStatus).IsEqualTo(ModelStatus.Available);
    }

    [Test]
    public async Task Get_PrivateBot_OnlyForMembersOfItsChat()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var handler = new GetBotHandler(w.Db, Substitute.For<IOpenRouterService>(), NoCap);

        var member = await handler.Handle(new GetBotQuery(w.Private.Id, w.Owner.Id), CancellationToken.None);
        var stranger = await handler.Handle(new GetBotQuery(w.Private.Id, w.Stranger.Id), CancellationToken.None);

        await Assert.That(member.Value!.IsPublic).IsFalse();
        await Assert.That(member.Value!.ChatId).IsEqualTo(w.Chat.Id);
        await Assert.That(stranger.IsFailure).IsTrue();
    }

    [Test]
    public async Task AddPrivateBotToAnotherChat_IsRefused()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var handler = new AddMemberHandler(w.Db, Substitute.For<IChatNotificationService>(), new ChatBotState(), Substitute.For<IOpenRouterService>(), NoCap);

        var result = await handler.Handle(new AddMemberCommand(w.OtherChat.Id, null, w.Private.Id, w.Owner.Id), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error!).Contains("private");
        await Assert.That(await w.Db.ChatMembers.AnyAsync(cm => cm.ChatId == w.OtherChat.Id && cm.BotId == w.Private.Id)).IsFalse();
    }

    [Test]
    public async Task ChatMembers_StillIncludePrivateBots()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var handler = new GetChatHandler(w.Db, new ChatBotState(), Substitute.For<IOpenRouterService>(), NoCap);

        var chat = await handler.Handle(new GetChatQuery(w.Chat.Id, w.Owner.Id), CancellationToken.None);

        await Assert.That(chat.Value!.Members.Where(m => m.Bot != null).Select(m => m.Bot!.Name)).IsEquivalentTo(["Public", "Private"]);
    }

    [Test]
    public async Task CreatePrivateBot_JoinsItsChat()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var notifications = Substitute.For<IChatNotificationService>();
        var handler = new CreateBotHandler(w.Db, Substitute.For<IOpenRouterService>(), NoCap, notifications);

        var result = await handler.Handle(new CreateBotCommand("Secret", "m/x", "x", null, 0.7, null, w.Owner.Id, IsPublic: false, ChatId: w.Chat.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value!.IsPublic).IsFalse();
        await Assert.That(result.Value!.ChatId).IsEqualTo(w.Chat.Id);
        await Assert.That(result.Value!.ChatCount).IsEqualTo(1);
        var saved = await w.Db.Bots.AsNoTracking().SingleAsync(b => b.Name == "Secret");
        await Assert.That(saved.IsPublic).IsFalse();
        await Assert.That(await w.Db.ChatMembers.AnyAsync(cm => cm.ChatId == w.Chat.Id && cm.BotId == saved.Id)).IsTrue();
        await notifications.Received(1).NotifyMemberAdded(w.Chat.Id, Arg.Is<ChatMemberDto>(m => m.BotId == saved.Id));
    }

    [Test]
    public async Task CreatePrivateBot_WithoutChat_IsRefused()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var handler = new CreateBotHandler(w.Db, Substitute.For<IOpenRouterService>(), NoCap, Substitute.For<IChatNotificationService>());

        var result = await handler.Handle(new CreateBotCommand("Secret", "m/x", "x", null, 0.7, null, w.Owner.Id, IsPublic: false), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
    }

    [Test]
    public async Task CreateBotInAChat_CallerMustBeAMember()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var handler = new CreateBotHandler(w.Db, Substitute.For<IOpenRouterService>(), NoCap, Substitute.For<IChatNotificationService>());

        var result = await handler.Handle(new CreateBotCommand("Secret", "m/x", "x", null, 0.7, null, w.Stranger.Id, IsPublic: false, ChatId: w.Chat.Id), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(await w.Db.Bots.AnyAsync(b => b.Name == "Secret")).IsFalse();
    }
}
