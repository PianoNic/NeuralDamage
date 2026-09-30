using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Bot names and nicknames are unique within a chat, not across the app: mentions, /mute and /kick
/// pick a bot by what it is called, so no name or nickname may belong to two bots in one chat,
/// ignoring case.
/// </summary>
internal static class BotNames
{
    /// <summary>A bot's name followed by its comma-separated nicknames, trimmed, without blanks.</summary>
    public static IEnumerable<string> Handles(string name, string? aliases) =>
        new[] { name }.Concat((aliases ?? "").Split(',')).Select(h => h.Trim()).Where(h => h.Length > 0);

    /// <summary>
    /// Why a bot called <paramref name="name"/> with nicknames <paramref name="aliases"/> cannot sit in
    /// <paramref name="chatId"/> next to its other bots (all but <paramref name="exceptBotId"/>), or null when it can.
    /// </summary>
    /// <param name="where">How the message names the chat, for a bot checked against several.</param>
    /// <remarks>Matched in memory: a chat has a handful of bots, and this keeps the comparison identical across database providers.</remarks>
    public static async Task<string?> ClashInChatAsync(NeuralDamageDbContext db, Guid chatId, string name, string? aliases, Guid? exceptBotId, CancellationToken ct, string where = "This chat")
    {
        var others = await db.ChatMembers
            .Where(cm => cm.ChatId == chatId && cm.BotId != null && cm.BotId != exceptBotId)
            .Select(cm => new { cm.Bot!.Name, cm.Bot.Aliases })
            .ToListAsync(ct);

        foreach (var handle in Handles(name, aliases))
            foreach (var other in others)
            {
                if (string.Equals(other.Name.Trim(), handle, StringComparison.OrdinalIgnoreCase))
                    return $"{where} already has a bot named {handle}.";
                if (Handles("", other.Aliases).Any(a => string.Equals(a, handle, StringComparison.OrdinalIgnoreCase)))
                    return $"{where} already has a bot nicknamed {handle} ({other.Name.Trim()}).";
            }
        return null;
    }
}
