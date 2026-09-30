using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure.Dtos;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Infrastructure;

/// <summary>
/// Goes through the orchestrator and the real decision engine rather than
/// calling Tier 1 directly: the rule was always right, but the orchestrator
/// never loaded the message it replies to, so the rule could not fire.
/// </summary>
public class BotReplyTriggerTests
{
    [Test]
    public async Task ReplyingToABot_MakesThatBotAnswer()
    {
        var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "owner@test.com", DisplayName = "Alice" };
        var gpt = new Bot { Name = "GPT", ModelId = "openai/gpt-4o", SystemPrompt = "x", CreatedById = user.Id };
        var claude = new Bot { Name = "Claude", ModelId = "anthropic/claude", SystemPrompt = "x", CreatedById = user.Id };
        var chat = new Chat { Name = "Chat", CreatedById = user.Id };
        db.AddRange(user, gpt, claude, chat);
        db.ChatMembers.AddRange(
            new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner },
            new ChatMember { ChatId = chat.Id, BotId = gpt.Id },
            new ChatMember { ChatId = chat.Id, BotId = claude.Id });

        var botMessage = new Message { ChatId = chat.Id, SenderBotId = claude.Id, Content = "pineapple belongs on pizza", CreatedAt = DateTime.UtcNow.AddMinutes(-10) };
        // Short, no name, no question: nothing but the reply link points at Claude.
        var reply = new Message { ChatId = chat.Id, SenderUserId = user.Id, Content = "no way", ReplyToId = botMessage.Id };
        db.Messages.AddRange(botMessage, reply);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var ranking = Substitute.For<IBotRankingService>();
        ranking.RankAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("{\"responders\": []}");
        var openRouter = Substitute.For<IOpenRouterService>();
        openRouter.GenerateResponseAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<List<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns("it absolutely does");
        var notifications = Substitute.For<IChatNotificationService>();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(notifications);
        services.AddSingleton(openRouter);
        services.AddSingleton<IBotDecisionEngine>(new BotDecisionEngine(
            db,
            new Tier3LlmJudge(ranking, NullLogger<Tier3LlmJudge>.Instance),
            NullLogger<BotDecisionEngine>.Instance));
        var provider = services.BuildServiceProvider();

        var orchestrator = new BotResponseOrchestrator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BotResponseOrchestrator>.Instance,
            InstantBotOptions.Create());

        await orchestrator.ProcessMessageAsync(chat.Id, reply.Id);

        await notifications.Received(1).NotifyMessageNew(chat.Id, Arg.Is<MessageDto>(m => m.SenderBotId == claude.Id));
        await notifications.DidNotReceive().NotifyMessageNew(chat.Id, Arg.Is<MessageDto>(m => m.SenderBotId == gpt.Id));
        db.Dispose();
    }
}
