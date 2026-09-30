using Mediator;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

/// <summary>
/// Bot names only have to be unique within a chat, and a private bot removed from its chat is deleted.
/// </summary>
public class ChatScopedBotTests
{
    private static readonly ModelPolicy NoCap = new(0, 0);

    private sealed record World(NeuralDamageDbContext Db, User Owner, Chat Chat, Chat OtherChat, Bot Rex, Bot Byte, Bot OtherRex);

    /// <summary>
    /// Chat holds the public Rex and the private Byte; OtherChat holds a second, unrelated public bot
    /// also called Rex.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com", DisplayName = "Alice" };
        var chat = new Chat { Name = "Home", CreatedById = owner.Id };
        var otherChat = new Chat { Name = "Elsewhere", CreatedById = owner.Id };
        var rex = new Bot { Name = "Rex", ModelId = "m/rex", SystemPrompt = "x", CreatedById = owner.Id };
        var otherRex = new Bot { Name = "Rex", ModelId = "m/other", SystemPrompt = "x", CreatedById = owner.Id };
        var byteBot = new Bot { Name = "Byte", ModelId = "m/byte", SystemPrompt = "x", CreatedById = owner.Id, IsPublic = false, ChatId = chat.Id };
        db.AddRange(owner, chat, otherChat, rex, otherRex, byteBot);
        db.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner },
            new ChatMember { ChatId = otherChat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner },
            new ChatMember { ChatId = chat.Id, BotId = rex.Id },
            new ChatMember { ChatId = chat.Id, BotId = byteBot.Id },
            new ChatMember { ChatId = otherChat.Id, BotId = otherRex.Id });
        await db.SaveChangesAsync();
        return new World(db, owner, chat, otherChat, rex, byteBot, otherRex);
    }

    private static CreateBotHandler Create(NeuralDamageDbContext db) =>
        new(db, Substitute.For<IOpenRouterService>(), NoCap, Substitute.For<IChatNotificationService>());

    private static AddMemberHandler Add(NeuralDamageDbContext db) =>
        new(db, Substitute.For<IChatNotificationService>(), new ChatBotState(), Substitute.For<IOpenRouterService>(), NoCap);

    private static CreateBotCommand NewBot(string name, Guid ownerId, bool isPublic = true, Guid? chatId = null, string? aliases = null) =>
        new(name, "m/x", "x", null, 0.7, null, aliases, ownerId, isPublic, chatId);

    [Test]
    public async Task CreatePublicBot_OutsideAChat_MayReuseAnyName()
    {
        var w = await SeedAsync();
        using var _ = w.Db;

        var result = await Create(w.Db).Handle(NewBot("rex", w.Owner.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await w.Db.Bots.CountAsync(b => b.Name.ToLower() == "rex")).IsEqualTo(3);
    }

    [Test]
    public async Task CreatePrivateBot_NameUsedInAnotherChat_IsAllowed()
    {
        var w = await SeedAsync();
        using var _ = w.Db;

        var result = await Create(w.Db).Handle(NewBot("Byte", w.Owner.Id, isPublic: false, chatId: w.OtherChat.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateBotIntoChat_NameTakenThere_IsRefused(bool isPublic)
    {
        var w = await SeedAsync();
        using var _ = w.Db;

        var result = await Create(w.Db).Handle(NewBot(" rEX ", w.Owner.Id, isPublic, w.Chat.Id), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error).IsEqualTo("This chat already has a bot named rEX.");
        await Assert.That(await w.Db.Bots.CountAsync()).IsEqualTo(3);
    }

    [Test]
    public async Task AddBot_NameTakenInTheChat_IsRefused()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var rexie = new Bot { Name = "REX", ModelId = "m", SystemPrompt = "x", CreatedById = w.Owner.Id };
        w.Db.Bots.Add(rexie);
        await w.Db.SaveChangesAsync();

        var result = await Add(w.Db).Handle(new AddMemberCommand(w.Chat.Id, null, rexie.Id, w.Owner.Id), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error).IsEqualTo("This chat already has a bot named REX.");
        await Assert.That(await w.Db.ChatMembers.AnyAsync(cm => cm.BotId == rexie.Id)).IsFalse();
    }

    [Test]
    public async Task AddBot_NameOnlyUsedInOtherChats_IsAllowed()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var third = new Chat { Name = "Third", CreatedById = w.Owner.Id };
        w.Db.Chats.Add(third);
        w.Db.ChatMembers.Add(new ChatMember { ChatId = third.Id, UserId = w.Owner.Id, Role = ChatMemberRole.Owner });
        await w.Db.SaveChangesAsync();

        var result = await Add(w.Db).Handle(new AddMemberCommand(third.Id, null, w.Rex.Id, w.Owner.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task RenameBot_ToANameTakenInOneOfItsChats_IsRefused()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var handler = new UpdateBotHandler(w.Db, Substitute.For<IOpenRouterService>(), NoCap, new ChatBotState());

        var clash = await handler.Handle(new UpdateBotCommand(w.Byte.Id, w.Owner.Id, "rex", null, null, null, null, null, null, null), CancellationToken.None);
        var ownCase = await handler.Handle(new UpdateBotCommand(w.Rex.Id, w.Owner.Id, "REX", null, null, null, null, null, null, null), CancellationToken.None);

        await Assert.That(clash.IsFailure).IsTrue();
        await Assert.That(clash.Error).IsEqualTo("This chat already has a bot named rex.");
        await Assert.That(ownCase.IsSuccess).IsTrue();
    }

    [Test]
    public async Task CreateBotIntoChat_NicknameEqualsABotsName_IsRefused()
    {
        var w = await SeedAsync();
        using var _ = w.Db;

        var result = await Create(w.Db).Handle(NewBot("Rover", w.Owner.Id, chatId: w.Chat.Id, aliases: "dog, REX"), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(result.Error).IsEqualTo("This chat already has a bot named REX.");
    }

    [Test]
    public async Task CreateBotIntoChat_NameOrNicknameEqualsABotsNickname_IsRefused()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        w.Byte.Aliases = "bitsy, nibble";
        await w.Db.SaveChangesAsync();

        var byName = await Create(w.Db).Handle(NewBot("Nibble", w.Owner.Id, chatId: w.Chat.Id), CancellationToken.None);
        var byNickname = await Create(w.Db).Handle(NewBot("Kilo", w.Owner.Id, chatId: w.Chat.Id, aliases: "BITSY"), CancellationToken.None);
        var elsewhere = await Create(w.Db).Handle(NewBot("Kilo", w.Owner.Id, chatId: w.OtherChat.Id, aliases: "bitsy"), CancellationToken.None);

        await Assert.That(byName.Error).IsEqualTo("This chat already has a bot nicknamed Nibble (Byte).");
        await Assert.That(byNickname.Error).IsEqualTo("This chat already has a bot nicknamed BITSY (Byte).");
        await Assert.That(elsewhere.IsSuccess).IsTrue();
    }

    [Test]
    public async Task AddBot_NicknameTakenInTheChat_IsRefused()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var kilo = new Bot { Name = "Kilo", ModelId = "m", SystemPrompt = "x", CreatedById = w.Owner.Id, Aliases = "k, byte" };
        w.Db.Bots.Add(kilo);
        await w.Db.SaveChangesAsync();

        var result = await Add(w.Db).Handle(new AddMemberCommand(w.Chat.Id, null, kilo.Id, w.Owner.Id), CancellationToken.None);

        await Assert.That(result.Error).IsEqualTo("This chat already has a bot named byte.");
        await Assert.That(await w.Db.ChatMembers.AnyAsync(cm => cm.BotId == kilo.Id)).IsFalse();
    }

    [Test]
    public async Task EditNicknames_ToOneTakenInTheChat_IsRefused_ButItsOwnAreFine()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        w.Rex.Aliases = "doggo";
        await w.Db.SaveChangesAsync();
        var handler = new UpdateBotHandler(w.Db, Substitute.For<IOpenRouterService>(), NoCap, new ChatBotState());

        var clash = await handler.Handle(new UpdateBotCommand(w.Byte.Id, w.Owner.Id, null, null, null, null, null, null, "bits, Doggo", null), CancellationToken.None);
        var own = await handler.Handle(new UpdateBotCommand(w.Rex.Id, w.Owner.Id, null, null, null, null, null, null, "doggo, rexy", null), CancellationToken.None);

        await Assert.That(clash.Error).IsEqualTo("This chat already has a bot nicknamed Doggo (Rex).");
        await Assert.That((await w.Db.Bots.AsNoTracking().SingleAsync(b => b.Id == w.Byte.Id)).Aliases).IsNull();
        await Assert.That(own.IsSuccess).IsTrue();
    }

    [Test]
    public async Task RenameBot_ToAnotherBotsNickname_IsRefused()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        w.Rex.Aliases = "doggo";
        await w.Db.SaveChangesAsync();
        var handler = new UpdateBotHandler(w.Db, Substitute.For<IOpenRouterService>(), NoCap, new ChatBotState());

        var result = await handler.Handle(new UpdateBotCommand(w.Byte.Id, w.Owner.Id, "DOGGO", null, null, null, null, null, null, null), CancellationToken.None);

        await Assert.That(result.Error).IsEqualTo("This chat already has a bot nicknamed DOGGO (Rex).");
    }

    [Test]
    public async Task Kick_PrivateBot_DeletesIt()
    {
        var w = await SeedAsync();
        using var _ = w.Db;

        var result = await new KickBotHandler(w.Db, Substitute.For<IChatNotificationService>())
            .Handle(new KickBotCommand(w.Chat.Id, w.Byte.Id, w.Owner.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await w.Db.ChatMembers.AnyAsync(cm => cm.BotId == w.Byte.Id)).IsFalse();
        await Assert.That((await w.Db.Bots.AsNoTracking().SingleAsync(b => b.Id == w.Byte.Id)).IsActive).IsFalse();
    }

    [Test]
    public async Task Kick_PublicBot_OnlyLeavesTheChat()
    {
        var w = await SeedAsync();
        using var _ = w.Db;

        var result = await new KickBotHandler(w.Db, Substitute.For<IChatNotificationService>())
            .Handle(new KickBotCommand(w.Chat.Id, w.Rex.Id, w.Owner.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await w.Db.ChatMembers.AnyAsync(cm => cm.BotId == w.Rex.Id)).IsFalse();
        await Assert.That((await w.Db.Bots.AsNoTracking().SingleAsync(b => b.Id == w.Rex.Id)).IsActive).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RemoveMember_DeletesOnlyAPrivateBot(bool isPublic)
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var bot = isPublic ? w.Rex : w.Byte;
        var memberId = await w.Db.ChatMembers.Where(cm => cm.ChatId == w.Chat.Id && cm.BotId == bot.Id).Select(cm => cm.Id).SingleAsync();

        var result = await new RemoveMemberHandler(w.Db, Substitute.For<IChatNotificationService>())
            .Handle(new RemoveMemberCommand(w.Chat.Id, memberId, w.Owner.Id), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await w.Db.ChatMembers.AnyAsync(cm => cm.Id == memberId)).IsFalse();
        await Assert.That((await w.Db.Bots.AsNoTracking().SingleAsync(b => b.Id == bot.Id)).IsActive).IsEqualTo(isPublic);
    }

    private static RunSlashCommandHandler Slash(World w, ChatBotState botState, IChatNotificationService notifications)
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<KickBotCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => new KickBotHandler(w.Db, notifications).Handle(call.Arg<KickBotCommand>(), call.Arg<CancellationToken>()));
        return new RunSlashCommandHandler(w.Db, sender, notifications, botState, Substitute.For<IBotResponseOrchestrator>());
    }

    [Test]
    public async Task SlashKick_PrivateBot_DeletesIt_AndSaysSo()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var notifications = Substitute.For<IChatNotificationService>();

        await Slash(w, new ChatBotState(), notifications).Handle(new RunSlashCommand(w.Chat.Id, w.Owner.Id, "/kick byte"), CancellationToken.None);

        await Assert.That((await w.Db.Bots.AsNoTracking().SingleAsync(b => b.Id == w.Byte.Id)).IsActive).IsFalse();
        await notifications.Received(1).NotifySystemMessage(w.Chat.Id, "Alice kicked Byte from the chat. Byte was private to this chat, so it was deleted.");
    }

    [Test]
    public async Task SlashCommands_ResolveNamesWithinTheChat()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var botState = new ChatBotState();
        var handler = Slash(w, botState, Substitute.For<IChatNotificationService>());

        await handler.Handle(new RunSlashCommand(w.OtherChat.Id, w.Owner.Id, "/mute rex"), CancellationToken.None);
        await handler.Handle(new RunSlashCommand(w.OtherChat.Id, w.Owner.Id, "/kick REX"), CancellationToken.None);

        await Assert.That(botState.IsMuted(w.OtherChat.Id, w.OtherRex.Id)).IsTrue();
        await Assert.That(botState.IsMuted(w.OtherChat.Id, w.Rex.Id)).IsFalse();
        await Assert.That(await w.Db.ChatMembers.AnyAsync(cm => cm.BotId == w.OtherRex.Id)).IsFalse();
        await Assert.That(await w.Db.ChatMembers.AnyAsync(cm => cm.ChatId == w.Chat.Id && cm.BotId == w.Rex.Id)).IsTrue();
    }
}
