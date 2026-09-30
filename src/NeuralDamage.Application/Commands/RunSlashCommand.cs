using Mediator;
using NeuralDamage.Application.Validators;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Models;
using NeuralDamage.Domain;

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
        ("/kick BotName", "Remove a bot from the chat"),
        ("/rename New Name", "Rename the chat"),
        ("/bots", "List the bots in this chat"),
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
    IBotResponseOrchestrator botOrchestrator) : ICommandHandler<RunSlashCommand, Result>
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

            case "/kick":
            {
                if (command.Argument.Length == 0)
                    return Mistake("Usage: /kick BotName");
                var bot = await FindBotAsync(chatId, command.Argument, ct);
                if (bot is null)
                    return Mistake($"Bot '{command.Argument}' not found in this chat.");

                var result = await sender.Send(new KickBotCommand(chatId, bot.Id, userId), ct);
                return result.IsSuccess ? $"{userName} kicked {bot.Name} from the chat." : Mistake(result.Error!);
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
