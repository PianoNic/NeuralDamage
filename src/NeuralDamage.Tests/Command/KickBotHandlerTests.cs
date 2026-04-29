using Microsoft.EntityFrameworkCore;
using NeuralDamage.Application.Command;
using NeuralDamage.Application.Interfaces;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class KickBotHandlerTests
{
    [Fact]
    public async Task Handle_OwnerCanKickBot()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();
        var user = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        db.Users.Add(user);
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "x", CreatedById = user.Id };
        db.Bots.Add(bot);
        var chat = new Chat { Name = "Chat", CreatedById = user.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, BotId = bot.Id });
        await db.SaveChangesAsync();

        var handler = new KickBotHandler(db, notifications);
        var result = await handler.Handle(new KickBotCommand(chat.Id, bot.Id, user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(await db.ChatMembers.AnyAsync(cm => cm.BotId == bot.Id && cm.ChatId == chat.Id));
    }

    [Fact]
    public async Task Handle_NonOwnerCannotKick()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();
        var owner = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        var member = new User { ExternalId = "ext-2", Email = "member@test.com" };
        db.Users.AddRange(owner, member);
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "x", CreatedById = owner.Id };
        db.Bots.Add(bot);
        var chat = new Chat { Name = "Chat", CreatedById = owner.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = owner.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = member.Id, Role = ChatMemberRole.Member });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, BotId = bot.Id });
        await db.SaveChangesAsync();

        var handler = new KickBotHandler(db, notifications);
        var result = await handler.Handle(new KickBotCommand(chat.Id, bot.Id, member.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True(await db.ChatMembers.AnyAsync(cm => cm.BotId == bot.Id && cm.ChatId == chat.Id));
    }

    [Fact]
    public async Task Handle_BotNotInChat_ReturnsFailure()
    {
        using var db = TestDbContext.Create();
        var notifications = Substitute.For<IChatNotificationService>();
        var user = new User { ExternalId = "ext-1", Email = "owner@test.com" };
        db.Users.Add(user);
        var chat = new Chat { Name = "Chat", CreatedById = user.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });
        await db.SaveChangesAsync();

        var handler = new KickBotHandler(db, notifications);
        var result = await handler.Handle(new KickBotCommand(chat.Id, Guid.NewGuid(), user.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("not in this chat", result.Error!);
    }
}
