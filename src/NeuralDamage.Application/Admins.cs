using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure;
using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Application;

/// <summary>
/// Admins may do anything any owner can. Whether someone is one is stored on their
/// user at every sign-in sync (<see cref="Domain.User.IsAdmin"/>); where the
/// configured <see cref="AppAdmins"/> are at hand, their emails count straight away.
/// </summary>
public static class Admins
{
    public static Task<bool> IsAdminAsync(NeuralDamageDbContext db, Guid userId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId && u.IsAdmin, ct);

    public static async Task<bool> IsAdminAsync(NeuralDamageDbContext db, AppAdmins admins, Guid userId, CancellationToken ct)
    {
        var user = await db.Users.Where(u => u.Id == userId).Select(u => new { u.Email, u.IsAdmin }).FirstOrDefaultAsync(ct);
        return user is not null && (user.IsAdmin || admins.IsAdmin(user.Email));
    }

    /// <summary>Whether the user owns the chat, or is an admin and may run it as if they did.</summary>
    public static async Task<bool> RunsChatAsync(NeuralDamageDbContext db, Guid chatId, Guid userId, CancellationToken ct) =>
        await db.ChatMembers.AnyAsync(cm => cm.ChatId == chatId && cm.UserId == userId && cm.Role == ChatMemberRole.Owner, ct)
        || await IsAdminAsync(db, userId, ct);

    /// <summary>Whether the user made the bot, or is an admin and may change it as if they had.</summary>
    public static async Task<bool> RunsBotAsync(NeuralDamageDbContext db, Domain.Bot bot, Guid userId, CancellationToken ct) =>
        bot.CreatedById == userId || await IsAdminAsync(db, userId, ct);
}
