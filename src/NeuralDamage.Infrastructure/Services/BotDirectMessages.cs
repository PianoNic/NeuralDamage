using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Mappers;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>A text a bot decided to send someone in its group chat, in their direct chat.</summary>
public record DirectMessageAsk(string To, string Text);

/// <summary>
/// Bots texting someone directly. In a group chat a bot may end its reply with
/// "[dm @Name: message]"; the tag leaves the group reply, and the message goes to the one direct chat
/// between that person and the bot, made the first time. Kept rare and safe: only public bots, only
/// to people in the chat who allow it, one per reply, and once per person per cooldown.
/// </summary>
public static partial class BotDirectMessages
{
    public const int MaxLength = 600;

    /// <summary>The line the chat prompt adds, when the bot may text people directly.</summary>
    public const string PromptLine =
        "- To text someone in this chat privately, the way people text each other directly, end your reply with [dm @TheirName: the message]. It goes to your direct chat with them, and the chat here doesn't see it. Do it only when it really fits, like when you said you'd text them, and rarely.";

    /// <summary>The reply without its dm tags, and what each asked to send; the first one only.</summary>
    public static (string Said, DirectMessageAsk? Ask) Extract(string reply)
    {
        DirectMessageAsk? ask = null;
        var said = DmTag().Replace(reply, match =>
        {
            var text = match.Groups["text"].Value.Trim();
            if (ask is null && text.Length > 0)
                ask = new DirectMessageAsk(match.Groups["to"].Value.Trim().TrimStart('@'), text.Length > MaxLength ? text[..MaxLength] : text);
            return "";
        });
        return (said.Trim(), ask);
    }

    /// <summary>Whether a bot may text people from <paramref name="chat"/>: a public bot in a group chat.</summary>
    public static bool CanSendFrom(Bot bot, Chat chat, BotBehaviorOptions options) =>
        options.DirectMessages && bot.IsActive && bot.IsPublic && bot.ChatId is null && !chat.IsDirect;

    /// <summary>
    /// Sends what the bot asked to, when it may: to a person in <paramref name="chat"/> by name or
    /// nickname, who allows it, and whom the bot hasn't texted within the cooldown. Returns the message
    /// sent, or null when it was not.
    /// </summary>
    public static async Task<Message?> SendAsync(
        NeuralDamageDbContext db, IChatNotificationService notifications, Bot bot, Chat chat, DirectMessageAsk ask,
        BotBehaviorOptions options, DateTime now, CancellationToken ct = default)
    {
        if (!CanSendFrom(bot, chat, options))
            return null;

        var people = await db.ChatMembers
            .Where(cm => cm.ChatId == chat.Id && cm.User != null)
            .Select(cm => cm.User!)
            .ToListAsync(ct);
        var person = people.FirstOrDefault(p =>
            string.Equals(PeopleNames.Handle(p), ask.To, StringComparison.OrdinalIgnoreCase)
            || PeopleNames.Split(p.Nicknames).Any(n => string.Equals(n, ask.To, StringComparison.OrdinalIgnoreCase)));
        if (person is null || !person.AllowBotDirectMessages)
            return null;

        var key = Chat.DirectKeyOf(person.Id, bot.Id);
        var since = now - options.DirectMessageCooldown;
        if (await db.Messages.AnyAsync(m => m.Chat.DirectKey == key && m.SenderBotId == bot.Id && m.CreatedAt >= since, ct))
            return null;

        var direct = await DirectChats.OpenAsync(db, notifications, person, bot.Id, otherIsBot: true, bot.Name, ct);
        if (direct is null)
            return null;

        var message = new Message { ChatId = direct.Id, SenderBotId = bot.Id, Content = ask.Text, CreatedAt = now };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        var dto = (await db.Messages.Include(m => m.SenderBot).AsNoTracking().FirstAsync(m => m.Id == message.Id, ct)).ToDto();
        await notifications.NotifyMessageNew(direct.Id, dto);
        // Their connection may not have joined a chat made just now; the web app drops the second copy.
        await notifications.NotifyUserMessageNew(person.Id, dto);
        return message;
    }

    [GeneratedRegex(@"\[\s*dm\s*(?:to\s+)?@?(?<to>[^\]:\n]{1,64}?)\s*:\s*(?<text>[^\]]{1,2000})\]", RegexOptions.IgnoreCase)]
    private static partial Regex DmTag();
}
