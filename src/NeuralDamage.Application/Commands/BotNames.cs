using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.Application.Commands;

/// <summary>
/// Bot names are unique within a chat, not across the app: mentions, /mute and /kick pick a bot by
/// name, so two bots in one chat must never share one, ignoring case.
/// </summary>
internal static class BotNames
{
    public static string Taken(string name) => $"This chat already has a bot named {name.Trim()}.";

    /// <summary>Whether a bot in <paramref name="chatId"/>, other than <paramref name="exceptBotId"/>, is called <paramref name="name"/>.</summary>
    /// <remarks>Matched in memory: a chat has a handful of bots, and this keeps the comparison identical across database providers.</remarks>
    public static async Task<bool> TakenInChatAsync(NeuralDamageDbContext db, Guid chatId, string name, Guid? exceptBotId, CancellationToken ct)
    {
        var names = await db.ChatMembers
            .Where(cm => cm.ChatId == chatId && cm.BotId != null && cm.BotId != exceptBotId)
            .Select(cm => cm.Bot!.Name)
            .ToListAsync(ct);
        return names.Any(n => string.Equals(n.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
