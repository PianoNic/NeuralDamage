using NeuralDamage.Application.Commands;
using NeuralDamage.Application.Queries;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class SetBotMutedHandlerTests
{
    [Test]
    public async Task MemberMutesAndUnmutes_ChatIsToldAndMembersShowIt()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "o@test.com", DisplayName = "Alice" };
        var member = new User { ExternalId = "ext-2", Email = "m@test.com", DisplayName = "Bob" };
        var bot = new Bot { Name = "GPT", ModelId = "m", SystemPrompt = "x", CreatedById = owner.Id };
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.AddRange(owner, member, bot, chat);
        db.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner },
            new ChatMember { ChatId = chat.Id, UserId = member.Id },
            new ChatMember { ChatId = chat.Id, BotId = bot.Id });
        await db.SaveChangesAsync();
        var state = new ChatBotState();
        var notifications = Substitute.For<IChatNotificationService>();
        var handler = new SetBotMutedHandler(db, state, notifications);
        var getChat = new GetChatHandler(db, state, Substitute.For<IOpenRouterService>(), new ModelPolicy(0, 0));

        var muted = await handler.Handle(new SetBotMutedCommand(chat.Id, bot.Id, member.Id, true), CancellationToken.None);
        var botEntry = (await getChat.Handle(new GetChatQuery(chat.Id, owner.Id), CancellationToken.None)).Value!.Members.Single(m => m.BotId == bot.Id);

        await Assert.That(muted.IsSuccess).IsTrue();
        await Assert.That(state.IsMuted(chat.Id, bot.Id)).IsTrue();
        await Assert.That(botEntry.IsMuted).IsTrue();
        await notifications.Received(1).NotifySystemMessage(chat.Id, "Bob muted GPT.");

        await handler.Handle(new SetBotMutedCommand(chat.Id, bot.Id, member.Id, false), CancellationToken.None);

        await Assert.That(state.IsMuted(chat.Id, bot.Id)).IsFalse();
        await notifications.Received(1).NotifySystemMessage(chat.Id, "Bob unmuted GPT.");
    }

    [Test]
    public async Task NonMember_IsRefused()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "o@test.com" };
        var stranger = new User { ExternalId = "ext-2", Email = "s@test.com" };
        var bot = new Bot { Name = "GPT", ModelId = "m", SystemPrompt = "x", CreatedById = owner.Id };
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.AddRange(owner, stranger, bot, chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, BotId = bot.Id });
        await db.SaveChangesAsync();
        var state = new ChatBotState();

        var result = await new SetBotMutedHandler(db, state, Substitute.For<IChatNotificationService>())
            .Handle(new SetBotMutedCommand(chat.Id, bot.Id, stranger.Id, true), CancellationToken.None);

        await Assert.That(result.IsFailure).IsTrue();
        await Assert.That(state.IsMuted(chat.Id, bot.Id)).IsFalse();
    }

    [Test]
    public async Task BotNotInChat_IsRefused()
    {
        using var db = TestDbContext.Create();
        var owner = new User { ExternalId = "ext-1", Email = "o@test.com" };
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.AddRange(owner, chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        await db.SaveChangesAsync();

        var result = await new SetBotMutedHandler(db, new ChatBotState(), Substitute.For<IChatNotificationService>())
            .Handle(new SetBotMutedCommand(chat.Id, Guid.NewGuid(), owner.Id, true), CancellationToken.None);

        await Assert.That(result.Error!).Contains("not in this chat");
    }
}
