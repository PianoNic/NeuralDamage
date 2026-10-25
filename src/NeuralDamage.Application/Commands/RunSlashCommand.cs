using Mediator;
using NeuralDamage.Application.Validators;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.Memory;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;

namespace NeuralDamage.Application.Commands;

/// <summary>A chat message starting with <c>/</c>, split into the command and whatever follows it.</summary>
public record SlashCommand(string Name, string Argument)
{
    /// <summary>What /help prints, in order.</summary>
    public static readonly IReadOnlyList<(string Usage, string Description)> Help =
    [
        ("/stop", "Pause all bots until the next message"),
        ("/mute BotName", "Stop a bot from replying or reacting"),
        ("/unmute BotName", "Let a muted bot talk again"),
        ("/clear", "Clear all messages in this chat"),
        ("/compact", "Fold older messages into the bots' memory, so their prompt stays small"),
        ("/kick BotName", "Remove a bot from the chat"),
        ("/rename New Name", "Rename the chat"),
        ("/bots", "List the bots in this chat"),
        ("/summarize", "Get a recap of the chat so far, just for you"),
        ("/remember BotName something", "Have a bot remember something for good in this chat"),
        ("/forget BotName something", "Have a bot forget what it remembers about that, in this chat"),
        ("/help", "Show available commands"),
    ];

    public static SlashCommand? Parse(string content)
    {
        var text = content.Trim();
        if (!text.StartsWith('/'))
            return null;

        var split = text.IndexOfAny([' ', '\t', '\r', '\n']);
        return split < 0
            ? new SlashCommand(text.ToLowerInvariant(), string.Empty)
            : new SlashCommand(text[..split].ToLowerInvariant(), text[split..].Trim());
    }
}

/// <remarks>
/// Only reached through <see cref="SendMessageCommand"/>, which has already
/// checked that the user is a member of the chat.
/// </remarks>
public record RunSlashCommand(Guid ChatId, Guid UserId, string Content) : ICommand<Result>;

/// <summary>
/// Runs a slash command and tells the chat what happened with a system message.
/// Errors are reported the same way rather than failing the request, so the
/// person who typed the command sees why it did nothing. Answers that only
/// concern that person - /help, /bots, an unknown command, a usage error or a
/// refusal - go to them alone.
/// </summary>
public class RunSlashCommandHandler(
    NeuralDamageDbContext db,
    ISender sender,
    IChatNotificationService notifications,
    IChatBotState botState,
    IBotResponseOrchestrator botOrchestrator,
    IChatCompactor compactor) : ICommandHandler<RunSlashCommand, Result>
{
    public async ValueTask<Result> Handle(RunSlashCommand request, CancellationToken cancellationToken)
    {
        var command = SlashCommand.Parse(request.Content)!;
        var feedback = await RunAsync(request.ChatId, request.UserId, command, cancellationToken);
        if (feedback is null)
            return Result.Success();

        if (feedback.CallerOnly)
            await notifications.NotifyUserSystemMessage(request.ChatId, request.UserId, feedback.Text);
        else
            await notifications.NotifySystemMessage(request.ChatId, feedback.Text);
        return Result.Success();
    }

    /// <summary>What a command answers: to the whole chat, or only to whoever typed it.</summary>
    private sealed record Feedback(string Text, bool CallerOnly = false)
    {
        public static implicit operator Feedback(string text) => new(text);
    }

    /// <summary>A command that did nothing: only whoever typed it needs to know why.</summary>
    private static Feedback Mistake(string text) => new(text, CallerOnly: true);

    /// <summary>What to tell the chat, or null when the command already did.</summary>
    private async Task<Feedback?> RunAsync(Guid chatId, Guid userId, SlashCommand command, CancellationToken ct)
    {
        var userName = await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "Someone";

        switch (command.Name)
        {
            case "/stop":
                botState.Stop(chatId);
                botOrchestrator.CancelPendingResponses(chatId);
                await notifications.NotifyBotResponseCancelled(chatId);
                return $"{userName} stopped all bots until the next message.";

            case "/mute":
            case "/unmute":
            {
                if (command.Argument.Length == 0)
                    return Mistake($"Usage: {command.Name} BotName");
                var bot = await FindBotAsync(chatId, command.Argument, ct);
                if (bot is null)
                    return Mistake($"Bot '{command.Argument}' not found in this chat.");

                if (command.Name == "/mute")
                {
                    botState.Mute(chatId, bot.Id);
                    return $"{userName} muted {bot.Name}.";
                }
                botState.Unmute(chatId, bot.Id);
                return $"{userName} unmuted {bot.Name}.";
            }

            case "/clear":
            {
                var result = await sender.Send(new ClearChatCommand(chatId, userId), ct);
                if (!result.IsSuccess)
                    return Mistake(result.Error!);

                // A reply still being written answers a message that no longer
                // exists, and saving it would fail on the reply link.
                botOrchestrator.CancelPendingResponses(chatId);
                await notifications.NotifyBotResponseCancelled(chatId);
                return $"{userName} cleared the chat.";
            }

            case "/compact":
            {
                var isOwner = await Admins.RunsChatAsync(db, chatId, userId, ct);
                if (!isOwner)
                    return Mistake("Only the chat owner can compact the chat.");

                int? folded;
                try
                {
                    folded = await compactor.CompactNowAsync(chatId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Mistake("Couldn't compact the chat: the summary model did not answer. Nothing was changed, try again in a moment.");
                }
                if (folded is null)
                    return Mistake("The chat is being compacted already. Try again in a moment.");
                if (folded == 0)
                    return Mistake("Nothing to compact: everything but the latest messages is in the bots' memory already.");

                // Every bot's history so far overlaps the new summary: each
                // starts over where the summary ends.
                botState.ForgetHistory(chatId);
                return $"{userName} compacted the chat: {folded} {(folded == 1 ? "message" : "messages")} folded into memory.";
            }

            case "/summarize":
                // The model takes a while, so the recap follows on its own.
                await notifications.NotifyUserSystemMessage(chatId, userId, "Summarizing…");
                _ = compactor.Summarize(chatId, userId);
                return null;

            case "/remember":
            case "/forget":
            {
                var (bot, what) = await SplitBotAsync(chatId, command.Argument, ct);
                if (bot is null || what.Length == 0)
                    return Mistake($"Usage: {command.Name} BotName something");
                return command.Name == "/remember"
                    ? await RememberAsync(chatId, bot, what, userName, ct)
                    : await ForgetAsync(chatId, bot, what, ct);
            }

            case "/kick":
            {
                if (command.Argument.Length == 0)
                    return Mistake("Usage: /kick BotName");
                var bot = await FindBotAsync(chatId, command.Argument, ct);
                if (bot is null)
                    return Mistake($"Bot '{command.Argument}' not found in this chat.");

                var result = await sender.Send(new KickBotCommand(chatId, bot.Id, userId), ct);
                if (!result.IsSuccess)
                    return Mistake(result.Error!);
                // Kicking a private bot deletes it (see PrivateBots).
                return bot.IsPublic
                    ? $"{userName} kicked {bot.Name} from the chat."
                    : $"{userName} kicked {bot.Name} from the chat. {bot.Name} was private to this chat, so it was deleted.";
            }

            case "/rename":
            {
                if (command.Argument.Length == 0)
                    return Mistake("Usage: /rename New Name");
                if (command.Argument.Length > UpdateChatValidator.MaxNameLength)
                    return Mistake(UpdateChatValidator.TooLongName);

                // Renaming tells the chat itself, however it is done.
                var result = await sender.Send(new UpdateChatCommand(chatId, command.Argument, userId), ct);
                return result.IsSuccess ? null : Mistake(result.Error!);
            }

            case "/bots":
            {
                var bots = await LoadBotsAsync(chatId, ct);
                if (bots.Count == 0)
                    return new Feedback("No bots in this chat.", CallerOnly: true);
                var lines = bots.Select(b => $"• {b.Name} ({b.ModelId})" + (botState.IsMuted(chatId, b.Id) ? " - muted" : ""));
                return new Feedback("Bots in this chat:\n" + string.Join('\n', lines), CallerOnly: true);
            }

            case "/help":
                return new Feedback("Available commands:\n" + string.Join('\n', SlashCommand.Help.Select(h => $"{h.Usage} - {h.Description}")), CallerOnly: true);

            default:
                return new Feedback($"Unknown command {command.Name}. Type /help to see available commands.", CallerOnly: true);
        }
    }

    /// <summary>
    /// A memory the bot keeps for good, pinned, as this chat's own: someone typed it in, so it is
    /// trusted, and kept to the chat it was said in.
    /// </summary>
    private async Task<Feedback> RememberAsync(Guid chatId, Bot bot, string what, string userName, CancellationToken ct)
    {
        if (!bot.MemoryEnabled)
            return Mistake($"{bot.Name}'s memory is off.");
        var memory = new BotMemory
        {
            BotId = bot.Id,
            Scope = MemoryScope.Chat,
            ChatId = chatId,
            StaysInChat = true,
            Source = MemorySource.Manual,
            Text = what,
            Importance = BotMemory.MaxImportance,
            Pinned = true,
        };
        return await BotMemories.AddAsync(db, memory, ct)
            ? $"{userName} asked {bot.Name} to remember: {memory.Text}"
            : Mistake($"{bot.Name} already remembers that.");
    }

    /// <summary>
    /// Forgets the memory of this chat, or about anyone, that shares the most words with what was
    /// typed; what was learned in another private chat is out of reach from here.
    /// </summary>
    private async Task<Feedback> ForgetAsync(Guid chatId, Bot bot, string what, CancellationToken ct)
    {
        var words = BotMemories.Words(what);
        var candidates = await db.BotMemories
            .Where(m => m.BotId == bot.Id && (!m.StaysInChat || m.ChatId == chatId) && (m.Scope != MemoryScope.Chat || m.ChatId == chatId))
            .ToListAsync(ct);
        var best = candidates
            .Select(m => (Memory: m, Overlap: BotMemories.Overlap(BotMemories.Words(m.Text), words)))
            .Where(x => x.Overlap >= 0.2 || x.Memory.Text.Contains(what, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Overlap)
            .Select(x => x.Memory)
            .FirstOrDefault();
        if (best is null)
            return Mistake($"{bot.Name} doesn't remember anything like that here.");
        db.BotMemories.Remove(best);
        await db.SaveChangesAsync(ct);
        return $"{bot.Name} forgot: {best.Text}";
    }

    /// <summary>"Rex Jr likes pizza" as the chat's bot Rex Jr and "likes pizza": the longest bot name the argument starts with.</summary>
    private async Task<(Bot? Bot, string What)> SplitBotAsync(Guid chatId, string argument, CancellationToken ct)
    {
        var bots = await LoadBotsAsync(chatId, ct);
        var bot = bots
            .Where(b => argument.StartsWith(b.Name, StringComparison.OrdinalIgnoreCase)
                && (argument.Length == b.Name.Length || !char.IsLetterOrDigit(argument[b.Name.Length])))
            .MaxBy(b => b.Name.Length);
        return bot is null ? (null, "") : (bot, argument[bot.Name.Length..].Trim().TrimStart(':', ',').Trim());
    }

    private Task<List<Bot>> LoadBotsAsync(Guid chatId, CancellationToken ct) =>
        db.ChatMembers
            .Where(cm => cm.ChatId == chatId && cm.BotId != null)
            .Select(cm => cm.Bot!)
            .OrderBy(b => b.Name)
            .AsNoTracking()
            .ToListAsync(ct);

    // Names are matched in memory: a chat has a handful of bots, and this keeps
    // the case-insensitive comparison identical across database providers.
    private async Task<Bot?> FindBotAsync(Guid chatId, string name, CancellationToken ct) =>
        (await LoadBotsAsync(chatId, ct)).FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
}
