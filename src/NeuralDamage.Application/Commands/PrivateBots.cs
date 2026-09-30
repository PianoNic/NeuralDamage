using Microsoft.EntityFrameworkCore;
using NeuralDamage.Infrastructure;

namespace NeuralDamage.Application.Commands;

internal static class PrivateBots
{
    /// <summary>
    /// A private bot belongs to its one chat and can join no other, so once removed from it nobody
    /// can reach it again: it is deleted the way <see cref="DeleteBotHandler"/> deletes a bot. The
    /// caller removes the membership and saves.
    /// </summary>
    /// <returns>Whether the bot was deleted.</returns>
    public static async Task<bool> RetireIfBoundToAsync(NeuralDamageDbContext db, Guid botId, Guid chatId, CancellationToken ct)
    {
        var bot = await db.Bots.FirstOrDefaultAsync(b => b.Id == botId, ct);
        if (bot is null || bot.IsPublic || bot.ChatId != chatId)
            return false;
        bot.IsActive = false;
        return true;
    }
}
