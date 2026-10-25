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
        Bot Bot,
        IChatCompactor Compactor);

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
        var compactor = Substitute.For<IChatCompactor>();
        var handler = new RunSlashCommandHandler(db, sender, notifications, botState, orchestrator, compactor);
        return new Harness(handler, db, sender, notifications, botState, orchestrator, chat.Id, user.Id, bot, compactor);
    }

    private static async Task RunAsync(Harness h, string content)
    {
        var result = await h.Handler.Handle(new RunSlashCommand(h.ChatId, h.UserId, content), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Remember_PinsAMemoryForThisChat_AndForget_DropsIt()
    {
        var h = await BuildAsync();
        using var _ = h.Db;

        await RunAsync(h, "/remember grumpy: Alice's birthday is on the 3rd of June");
        var memory = await h.Db.BotMemories.SingleAsync();
        await Assert.That(memory.Text).IsEqualTo("Alice's birthday is on the 3rd of June");
        await Assert.That(memory.Pinned).IsTrue();
        await Assert.That(memory.ChatId).IsEqualTo(h.ChatId);
        await h.Notifications.Received(1).NotifySystemMessage(h.ChatId, "Alice asked Grumpy to remember: Alice's birthday is on the 3rd of June");

        await RunAsync(h, "/forget Grumpy birthday june");
        await Assert.That(await h.Db.BotMemories.AnyAsync()).IsFalse();
        await h.Notifications.Received(1).NotifySystemMessage(h.ChatId, "Grumpy forgot: Alice's birthday is on the 3rd of June");
    }

    [Test]
    public async Task Remember_WithoutABotOrAThing_ExplainsHow()
    {
        var h = await BuildAsync();
        using var _ = h.Db;

        await RunAsync(h, "/remember nobody likes tea");
        await RunAsync(h, "/remember Grumpy");

        await h.Notifications.Received(2).NotifyUserSystemMessage(h.ChatId, h.UserId, "Usage: /remember BotName something");
        await Assert.That(await h.Db.BotMemories.AnyAsync()).IsFalse();
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
    public async Task Mute_UnknownBot_TellsOnlyTheCaller()
    {
        var h = await BuildAsync();
        using var _ = h.Db;

        await RunAsync(h, "/mute Nobody");

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, h.UserId, "Bot 'Nobody' not found in this chat.");
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
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
    public async Task Clear_Refused_TellsOnlyTheCallerWhy()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.Sender.Send(Arg.Any<ClearChatCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Only the chat owner can clear messages."));

        await RunAsync(h, "/clear");

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, h.UserId, "Only the chat owner can clear messages.");
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
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
        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, h.UserId, NeuralDamage.Application.Validators.UpdateChatValidator.TooLongName);
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
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
    public async Task Help_ListsCompactAndSummarize()
    {
        await Assert.That(SlashCommand.Help.Select(c => c.Usage)).Contains("/compact");
        await Assert.That(SlashCommand.Help.Select(c => c.Usage)).Contains("/summarize");
    }

    [Test]
    public async Task Compact_ByAnyoneButTheOwner_IsRefused_ToTheCallerOnly()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        var bob = new User { ExternalId = "ext-2", Email = "bob@test.com", DisplayName = "Bob" };
        h.Db.Users.Add(bob);
        h.Db.ChatMembers.Add(new ChatMember { ChatId = h.ChatId, UserId = bob.Id, Role = ChatMemberRole.Member });
        await h.Db.SaveChangesAsync();

        var result = await h.Handler.Handle(new RunSlashCommand(h.ChatId, bob.Id, "/compact"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, bob.Id, "Only the chat owner can compact the chat.");
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
        await h.Compactor.DidNotReceiveWithAnyArgs().CompactNowAsync(default, default);
    }

    [Test]
    public async Task Compact_NothingToFold_TellsTheCallerWhy()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.Compactor.CompactNowAsync(h.ChatId, Arg.Any<CancellationToken>()).Returns(0);

        await RunAsync(h, "/compact");

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, h.UserId, Arg.Is<string>(s => s.StartsWith("Nothing to compact")));
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
    }

    [Test]
    public async Task Compact_WhileAlreadyCompacting_TellsTheCaller()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.Compactor.CompactNowAsync(h.ChatId, Arg.Any<CancellationToken>()).Returns((int?)null);

        await RunAsync(h, "/compact");

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, h.UserId, "The chat is being compacted already. Try again in a moment.");
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
    }

    [Test]
    public async Task Compact_ModelFails_TellsTheCaller()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.Compactor.CompactNowAsync(h.ChatId, Arg.Any<CancellationToken>()).Returns<int?>(_ => throw new HttpRequestException("down"));

        await RunAsync(h, "/compact");

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, h.UserId, Arg.Is<string>(s => s.StartsWith("Couldn't compact the chat")));
        await h.Notifications.DidNotReceiveWithAnyArgs().NotifySystemMessage(default, default!);
    }

    [Test]
    public async Task Compact_TellsTheChat_AndStartsEveryBotsHistoryOver()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.Compactor.CompactNowAsync(h.ChatId, Arg.Any<CancellationToken>()).Returns(214);
        h.BotState.SetHistoryStart(h.ChatId, h.Bot.Id, DateTime.UtcNow.AddDays(-1));
        h.BotState.SetHistorySummary(h.ChatId, h.Bot.Id, new SummarySeen("old", DateTime.UtcNow.AddDays(-2)));

        await RunAsync(h, "/compact");

        await h.Notifications.Received(1).NotifySystemMessage(h.ChatId, "Alice compacted the chat: 214 messages folded into memory.");
        await Assert.That(h.BotState.HistoryStart(h.ChatId, h.Bot.Id)).IsNull();
        await Assert.That(h.BotState.HistorySummary(h.ChatId, h.Bot.Id)).IsNull();
    }

    [Test]
    public async Task Summarize_GoesToTheCallerOnly_AndRunsInTheBackground()
    {
        var h = await BuildAsync();
        using var _ = h.Db;
        h.Compactor.Summarize(h.ChatId, h.UserId).Returns(new TaskCompletionSource().Task);

        await RunAsync(h, "/summarize");

        await h.Notifications.Received(1).NotifyUserSystemMessage(h.ChatId, h.UserId, "Summarizing…");
        await h.Compactor.Received(1).Summarize(h.ChatId, h.UserId);
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
