using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>
/// Covers the emoji reactions bots leave on messages they chose not to answer.
/// The service that picks the emoji was always there; nothing called it, so
/// these assert the orchestrator actually reaches it.
/// </summary>
public class BotReactionOrchestrationTests
{
    /// <summary>
    /// Reacting is deliberately occasional, so a single run proves nothing -
    /// the odds of no reaction across this many are about one in ten million.
    /// </summary>
    private const int Runs = 100;

    private sealed record Harness(
        BotResponseOrchestrator Orchestrator,
        NeuralDamageDbContext Db,
        IChatNotificationService Notifications,
        IBotDecisionEngine Decisions,
        IOpenRouterService OpenRouter,
        Guid ChatId,
        Bot Bot);

    private static async Task<Harness> BuildAsync()
    {
        var db = TestDbContext.Create();

        var user = new User { ExternalId = "ext-1", Email = "owner@test.com", DisplayName = "Alice" };
        db.Users.Add(user);
        var bot = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "x", CreatedById = user.Id };
        db.Bots.Add(bot);
        var chat = new Chat { Name = "Chat", CreatedById = user.Id };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, BotId = bot.Id });
        await db.SaveChangesAsync();

        var notifications = Substitute.For<IChatNotificationService>();
        var decisions = Substitute.For<IBotDecisionEngine>();
        var openRouter = Substitute.For<IOpenRouterService>();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(notifications);
        services.AddSingleton(decisions);
        services.AddSingleton(openRouter);
        var provider = services.BuildServiceProvider();

        var orchestrator = new BotResponseOrchestrator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BotResponseOrchestrator>.Instance);

        return new Harness(orchestrator, db, notifications, decisions, openRouter, chat.Id, bot);
    }

    /// <summary>A fresh message each run, so the duplicate guard never hides a reaction.</summary>
    private static async Task<Message> AddMessageAsync(NeuralDamageDbContext db, Guid chatId, string content)
    {
        var user = await db.Users.FirstAsync();
        var message = new Message { ChatId = chatId, SenderUserId = user.Id, Content = content };
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    [Test]
    public async Task SilentBot_Reacts_AndBroadcasts()
    {
        var h = await BuildAsync();
        // Nobody answers, so the bot is free to react instead.
        h.Decisions.DecideRespondersAsync(Arg.Any<Guid>(), Arg.Any<Message>(), Arg.Any<List<Bot>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        for (var i = 0; i < Runs; i++)
        {
            var message = await AddMessageAsync(h.Db, h.ChatId, $"that is hilarious {i}");
            await h.Orchestrator.ProcessMessageAsync(h.ChatId, message.Id);
        }

        var reactions = await h.Db.Reactions.ToListAsync();
        await Assert.That(reactions).IsNotEmpty();
        await Assert.That(reactions.All(r => r.BotId == h.Bot.Id)).IsTrue();
        await Assert.That(reactions.All(r => r.UserId is null)).IsTrue();
        // The keyword map owns the choice; the orchestrator must not override it.
        await Assert.That(reactions.All(r => r.Emoji == "😂")).IsTrue();

        await h.Notifications.ReceivedWithAnyArgs().NotifyReactionUpdated(default, default, default!);
        // A silent bot must stay silent otherwise.
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifyMessageNew(default, default!);
        h.Db.Dispose();
    }

    [Test]
    public async Task RespondingBot_DoesNotAlsoReact()
    {
        var h = await BuildAsync();
        h.Decisions.DecideRespondersAsync(Arg.Any<Guid>(), Arg.Any<Message>(), Arg.Any<List<Bot>>(), Arg.Any<CancellationToken>())
            .Returns([h.Bot.Id]);
        h.OpenRouter.GenerateResponseAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<List<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns("sure thing");

        for (var i = 0; i < Runs; i++)
        {
            var message = await AddMessageAsync(h.Db, h.ChatId, $"that is hilarious {i}");
            await h.Orchestrator.ProcessMessageAsync(h.ChatId, message.Id);
        }

        await Assert.That(await h.Db.Reactions.AnyAsync()).IsFalse();
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifyReactionUpdated(default, default, default!);
        h.Db.Dispose();
    }
}
