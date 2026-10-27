using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Domain.Enums;
using NeuralDamage.Infrastructure.Mappers;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// The one direct chat between a person and a bot or another person: found by its
/// <see cref="Chat.DirectKey"/>, or made. Shared by people opening a chat and bots deciding to text.
/// </summary>
public static class DirectChats
{
    /// <summary>
    /// The direct chat between <paramref name="person"/> and <paramref name="otherId"/> (a bot when
    /// <paramref name="otherIsBot"/>, else a person), made and announced the first time. Someone who
    /// left their side of it is back in. Null only when it could not be made.
    /// </summary>
    public static async Task<Chat?> OpenAsync(
        NeuralDamageDbContext db, IChatNotificationService notifications, User person, Guid otherId, bool otherIsBot, string name, CancellationToken ct = default)
    {
        var key = Chat.DirectKeyOf(person.Id, otherId);
        var chat = await db.Chats.FirstOrDefaultAsync(c => c.DirectKey == key, ct);
        if (chat is not null)
        {
            await RejoinAsync(db, notifications, chat, person.Id, ct);
            return chat;
        }

        chat = new Chat { Name = name, CreatedById = person.Id, DirectKey = key };
        db.Chats.Add(chat);
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = person.Id, Role = ChatMemberRole.Owner });
        db.ChatMembers.Add(otherIsBot
            ? new ChatMember { ChatId = chat.Id, BotId = otherId }
            : new ChatMember { ChatId = chat.Id, UserId = otherId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Opened twice at once: the other request made it, so that one is the chat.
            db.ChangeTracker.Clear();
            return await db.Chats.FirstOrDefaultAsync(c => c.DirectKey == key, ct);
        }

        var dto = chat.ToDto();
        await notifications.NotifyUserChatCreated(person.Id, dto);
        if (!otherIsBot)
            await notifications.NotifyUserChatCreated(otherId, dto);
        return chat;
    }

    private static async Task RejoinAsync(NeuralDamageDbContext db, IChatNotificationService notifications, Chat chat, Guid userId, CancellationToken ct)
    {
        if (await db.ChatMembers.AnyAsync(cm => cm.ChatId == chat.Id && cm.UserId == userId, ct))
            return;
        db.ChatMembers.Add(new ChatMember { ChatId = chat.Id, UserId = userId, Role = ChatMemberRole.Member });
        await db.SaveChangesAsync(ct);
        await notifications.NotifyUserChatCreated(userId, chat.ToDto());
    }
}
