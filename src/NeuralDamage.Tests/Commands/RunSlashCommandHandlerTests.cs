using Mediator;
using NeuralDamage.Application.Commands;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Tests.Helpers;
using NSubstitute;

namespace NeuralDamage.Tests.Commands;

public class SlashCommandParserTests
{
    [Test]
    [Arguments("/help", "/help", "")]
    [Arguments("/MUTE Grumpy", "/mute", "Grumpy")]
    [Arguments("  /rename   The New Name  ", "/rename", "The New Name")]
    [Arguments("/kick\tGrumpy", "/kick", "Grumpy")]
    [Arguments("/", "/", "")]
    public async Task Parse_SplitsCommandAndArgument(string content, string name, string argument)
    {
        var command = SlashCommand.Parse(content);

        await Assert.That(command).IsEqualTo(new SlashCommand(name, argument));
    }

    [Test]
    [Arguments("hello")]
    [Arguments("a/b")]
    [Arguments("")]
    public async Task Parse_NonCommand_ReturnsNull(string content)
    {
        await Assert.That(SlashCommand.Parse(content)).IsNull();
    }
}

public class RunSlashCommandHandlerTests
{
    private sealed record Harness(
        RunSlashCommandHandler Handler,
        NeuralDamageDbContext Db,
        ISender Sender,
        IChatNotificationService Notifications,
        IChatBotState BotState,
        IBotResponseOrchestrator Orchestrator,
        Guid ChatId,
        Guid UserId,
        Bot Bot);

    private static async Task<Harness> BuildAsync()
    {
        var db = TestDbContext.Create();
        var user = new User { ExternalId = "ext-1", Email = "alice@test.com", DisplayName = "Alice" };
        db.Users.Add(user);
        var chat = new Chat { Name = "General", CreatedById = user.Id };
        db.Chats.Add(chat);
        var bot = new Bot { Name = "Grumpy", ModelId = "openai/gpt-4o", SystemPrompt = "x", CreatedById = user.Id };
        db.Bots.Add(bot);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = user.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, BotId = bot.Id });
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        var notifications = Substitute.For<IChatNotificationService>();
        var orchestrator = Substitute.For<IBotResponseOrchestrator>();
        var botState = new ChatBotState();
        var handler = new RunSlashCommandHandler(db, sender, notifications, botState, orchestrator);
        return new Harness(handler, db, sender, notifications, botState, orchestrator, chat.Id, user.Id, bot);
    }

    private static async Task RunAsync(Harness h, string content)
    {
        var result = await h.Handler.Handle(new RunSlashCommand(h.ChatId, h.UserId, content), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Mute_And_Unmute_ToggleTheBot()
    {
        var h = await BuildAsync();
        using var _ = h.Db;

        await RunAsync(h, "/mute grumpy");
        await Assert.That(h.BotState.IsMuted(h.ChatId, h.Bot.Id)).IsTrue();
        await h.Notifications.Received(1).NotifySystemMessage(h.ChatId, "Alice muted Grumpy.");

        await RunAsync(h, "/unmute Grumpy");
        await Assert.That(h.BotState.IsMuted(h.ChatId, h.Bot.Id)).IsFalse();
        await h.Notifications.Received(1).NotifySystemMessage(h.ChatId, "Alice unmuted Grumpy.");
    }

    [Test]
    public async Task Mute_UnknownBot_ReportsNotFound()
    {
        var h = await BuildAsync();
        using var _ = h.Db;

        await RunAsync(h, "/mute Nobody");

        await h.Notifications.Received(1).NotifySystemMessage(h.ChatId, "Bot 'Nobody' not found in this chat.");
    }

    [Test]
    public async Task Stop_SilencesChat_AndCancelsReplies()
    {
        var h = await BuildAsync();
        using var _ = h.Db;

        await RunAsync(h, "/stop");

        await Assert.That(h.BotState.IsStopped(h.ChatId)).IsTrue();
        h.Orchestrator.Received(1).CancelPendingResponses(h.ChatId);
        await h.Notifications.Received(1).NotifyBotResponseCancelled(h.ChatId);
    }

    [Test]
    public async Task Kick_DispatchesKickBotCommand_ByName()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.Sender.Send(Arg.Any<KickBotCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        await RunAsync(h, "/kick GRUMPY");

        await h.Sender.Received(1).Send(new KickBotCommand(h.ChatId, h.Bot.Id, h.UserId), Arg.Any<CancellationToken>());
        await h.Notifications.Received(1).NotifySystemMessage(h.ChatId, "Alice kicked Grumpy from the chat.");
    }

    [Test]
    public async Task Clear_Refused_ReportsTheReason()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.Sender.Send(Arg.Any<ClearChatCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Only the chat owner can clear messages."));

        await RunAsync(h, "/clear");

        await h.Notifications.Received(1).NotifySystemMessage(h.ChatId, "Only the chat owner can clear messages.");
    }

    [Test]
    public async Task Clear_CancelsRepliesStillBeingWritten()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.Sender.Send(Arg.Any<ClearChatCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        await RunAsync(h, "/clear");

        h.Orchestrator.Received(1).CancelPendingResponses(h.ChatId);
        await h.Notifications.Received(1).NotifyBotResponseCancelled(h.ChatId);
        await h.Notifications.Received(1).NotifySystemMessage(h.ChatId, "Alice cleared the chat.");
    }

    [Test]
    public async Task Rename_DispatchesUpdateChatCommand()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.Sender.Send(Arg.Any<UpdateChatCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        await RunAsync(h, "/rename Bot Party");

        await h.Sender.Received(1).Send(new UpdateChatCommand(h.ChatId, "Bot Party", h.UserId), Arg.Any<CancellationToken>());
        // The rename itself tells the chat, so the command does not say it twice.
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
    }

    [Test]
    public async Task Rename_TooLong_SaysWhatTheMenuSays()
    {
        var h = await BuildAsync();
        using var _ = h.Db;

        await RunAsync(h, "/rename " + new string('x', 257));

        await h.Sender.DidNotReceiveWithAnyArgs().Send(default(UpdateChatCommand)!, default);
        await h.Notifications.Received(1).NotifySystemMessage(h.ChatId, NeuralDamage.Application.Validators.UpdateChatValidator.TooLongName);
    }

    [Test]
    public async Task Bots_ListsBotsAndMuteState()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.BotState.Mute(h.ChatId, h.Bot.Id);

        await RunAsync(h, "/bots");

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, h.UserId, "Bots in this chat:\n• Grumpy (openai/gpt-4o) - muted");
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
    }

    [Test]
    public async Task Help_ListsEveryCommand()
    {
        var h = await BuildAsync();
        using var _ = h.Db;

        await RunAsync(h, "/help");

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, h.UserId, Arg.Is<string>(s =>
            SlashCommand.Help.All(c => s.Contains(c.Usage))));
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
    }

    [Test]
    public async Task UnknownCommand_PointsToHelp()
    {
        var h = await BuildAsync();
        using var _ = h.Db;

        await RunAsync(h, "/dance");

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, h.UserId, "Unknown command /dance. Type /help to see available commands.");
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
    }
}
