using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NSubstitute;

namespace NeuralDamage.Tests.Helpers;

/// <summary>
/// A chat with one person and a few bots, wired to a real orchestrator. Each
/// scope gets its own context over one in-memory database, as in production,
/// because reactions run in a scope of their own alongside the replies.
/// </summary>
public sealed class OrchestratorHarness : IDisposable
{
    public required BotResponseOrchestrator Orchestrator { get; init; }
    public required NeuralDamageDbContext Db { get; init; }
    public required IChatNotificationService Notifications { get; init; }
    public required IBotDecisionEngine Decisions { get; init; }
    public required IOpenRouterService OpenRouter { get; init; }
    public required ChatBotState BotState { get; init; }
    public required ServiceProvider Provider { get; init; }
    public required User User { get; init; }
    public required Chat Chat { get; init; }
    public required List<Bot> Bots { get; init; }

    public static async Task<OrchestratorHarness> CreateAsync(
        int botCount = 1,
        IOptions<BotBehaviorOptions>? options = null,
        Action<IServiceCollection>? configure = null)
    {
        var dbOptions = new DbContextOptionsBuilder<NeuralDamageDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new NeuralDamageDbContext(dbOptions);

        var user = new User { ExternalId = "ext-1", Email = "owner@test.com", DisplayName = "Alice" };
        var chat = new Chat { Name = "Chat", CreatedById = user.Id };
        db.AddRange(user, chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });
        string[] names = ["GPT", "Claude", "Gemini", "Llama"];
        var bots = names.Take(botCount)
            .Select(n => new Bot { Name = n, ModelId = $"model/{n.ToLowerInvariant()}", SystemPrompt = "x", CreatedById = user.Id })
            .ToList();
        db.Bots.AddRange(bots);
        db.ChatMembers.AddRange(bots.Select(b => new ChatMember { ChatId = chat.Id, BotId = b.Id }));
        await db.SaveChangesAsync();

        var notifications = Substitute.For<IChatNotificationService>();
        var decisions = Substitute.For<IBotDecisionEngine>();
        var openRouter = Substitute.For<IOpenRouterService>();
        options ??= InstantBotOptions.Create();

        var services = new ServiceCollection();
        services.AddScoped(_ => new NeuralDamageDbContext(dbOptions));
        services.AddSingleton(notifications);
        services.AddSingleton(decisions);
        services.AddSingleton(openRouter);
        services.AddSingleton(new ModelPolicy(0, 0));
        services.AddSingleton(options);
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();

        var botState = new ChatBotState();
        var orchestrator = new BotResponseOrchestrator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            botState,
            NullLogger<BotResponseOrchestrator>.Instance,
            options);

        return new OrchestratorHarness
        {
            Orchestrator = orchestrator,
            Db = db,
            Notifications = notifications,
            Decisions = decisions,
            OpenRouter = openRouter,
            BotState = botState,
            Provider = provider,
            User = user,
            Chat = chat,
            Bots = bots,
        };
    }

    public void Respond(params Bot[] bots) =>
        Decisions.DecideRespondersAsync(Arg.Any<Guid>(), Arg.Any<Message>(), Arg.Any<List<Bot>>(), Arg.Any<CancellationToken>())
            .Returns(bots.Select(b => b.Id).ToList());

    public void Reply(params string[] replies) =>
        OpenRouter.GenerateResponseAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<List<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(replies[0], replies[1..]);

    public async Task<Message> SayAsync(string content, Bot? asBot = null, Guid? replyToId = null, DateTime? at = null)
    {
        var message = new Message
        {
            ChatId = Chat.Id,
            SenderUserId = asBot is null ? User.Id : null,
            SenderBotId = asBot?.Id,
            Content = content,
            ReplyToId = replyToId,
            CreatedAt = at ?? DateTime.UtcNow,
        };
        Db.Messages.Add(message);
        await Db.SaveChangesAsync();
        return message;
    }

    /// <summary>Messages as stored, read fresh rather than from this context's cache.</summary>
    public Task<List<Message>> MessagesFromAsync(Bot bot) =>
        Db.Messages.AsNoTracking().Where(m => m.SenderBotId == bot.Id).OrderBy(m => m.CreatedAt).ToListAsync();

    public void Dispose()
    {
        Provider.Dispose();
        Db.Dispose();
    }
}
