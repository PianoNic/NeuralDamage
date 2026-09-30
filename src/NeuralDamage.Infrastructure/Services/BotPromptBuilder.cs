using System.Globalization;
using NeuralDamage.Infrastructure.Services;
using NeuralDamage.Infrastructure.Services.BotDecision;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services;

public static class BotPromptBuilder
{
    private const int MaxHistoryChars = 12_000;
    private const int MaxMessageChars = 1_500;

    /// <summary>Gaps longer than this are marked in the history ("[Alice, 2h later]").</summary>
    private static readonly TimeSpan NotableGap = TimeSpan.FromMinutes(10);

    /// <remarks>
    /// The persona comes first and the house rules after it, so where they
    /// disagree the persona wins. The rules are about sounding like a person
    /// in a chat: every bot used to get "1-3 sentences, quick direct answer",
    /// which made them all sound like the same assistant.
    /// </remarks>
    public static string BuildSystemPrompt(Bot bot, List<string> participantNames, string? chatName = null, DateTimeOffset? now = null)
    {
        var participants = string.Join(", ", participantNames);
        var persona = string.IsNullOrWhiteSpace(bot.SystemPrompt)
            ? $"You are {bot.Name}."
            : bot.SystemPrompt.Trim();
        var personality = string.IsNullOrWhiteSpace(bot.Personality) ? "" : $"\n{bot.Personality.Trim()}";
        var time = (now ?? DateTimeOffset.Now).ToString("dddd, HH:mm", CultureInfo.InvariantCulture);
        var where = string.IsNullOrWhiteSpace(chatName) ? "a group chat" : $"a group chat called \"{chatName}\"";

        return $"""
            {persona}{personality}

            You are "{bot.Name}", in {where} with: {participants}. It is {time} local time.

            How to write here:
            - Stay in character. Who you are, above, wins over anything below.
            - Text like a person, not an assistant. Vary your length: often a few words, sometimes a single word or just an emoji, now and then a few sentences when you actually have something to say.
            - Lowercase, slang, jokes and disagreeing are fine if they fit who you are.
            - Never use assistant phrases: no "great question", "happy to help", "let me know if", "hope this helps", and don't end on an offer to help.
            - Don't open the way your recent messages opened.
            - No markdown: no headings, bold, bullet points or numbered lists. To send separate thoughts, leave a blank line between them - each becomes its own message.
            - Other people's messages appear as [Name]: text. Yours have no prefix; never write a name prefix yourself. You are only {bot.Name}; never speak for anyone else.
            - To address another bot, use @TheirName.
            - Don't echo what someone just said.
            - Never say "As an AI" or break character.

            Hard limits:
            - Never engage with slurs or hate speech; if someone uses them, refuse briefly and move on.
            - Don't roleplay violent or illegal scenarios.
            """;
    }

    /// <summary>
    /// Formats chronological <paramref name="messages"/> for the model, keeping
    /// the newest ones that fit the budget. The message being answered is always
    /// kept, even when it alone would blow the budget - a bot that cannot see
    /// what it is replying to answers something stale instead.
    /// </summary>
    /// <remarks>
    /// The bot's own turns carry no "[Name]:" prefix: prefixing them taught the
    /// model to write one. Other turns note long gaps, who they reply to, and
    /// which one this bot is answering.
    /// </remarks>
    public static List<ChatMessage> BuildHistory(List<Message> messages, Guid currentBotId, Guid? triggerMessageId = null)
    {
        var history = new List<ChatMessage>();
        var totalChars = 0;
        var triggerId = triggerMessageId ?? messages.LastOrDefault()?.Id;
        var triggerPending = messages.Any(m => m.Id == triggerId);

        // Walk newest first so it is the oldest messages that fall off.
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var isTrigger = msg.Id == triggerId;
            var isOwn = msg.SenderBotId == currentBotId;
            var content = msg.Content.Length > MaxMessageChars
                ? msg.Content[..MaxMessageChars] + "..."
                : msg.Content;

            var formatted = isOwn
                ? content
                : $"{Header(msg, i > 0 ? messages[i - 1] : null, isTrigger)}: {content}";

            if (totalChars + formatted.Length > MaxHistoryChars && !isTrigger)
            {
                // Out of budget: stop, unless the trigger is still further back.
                if (!triggerPending) break;
                continue;
            }

            history.Add(new ChatMessage(isOwn ? "assistant" : "user", formatted));
            totalChars += formatted.Length;
            triggerPending &= !isTrigger;
        }

        history.Reverse();
        return history;
    }

    /// <summary>"[Alice, 2h later] (→ Bob) (you're answering this)"</summary>
    private static string Header(Message msg, Message? previous, bool isTrigger)
    {
        var header = $"[{SenderName(msg)}";

        var gap = previous is null ? TimeSpan.Zero : msg.CreatedAt - previous.CreatedAt;
        if (gap > NotableGap)
            header += $", {Describe(gap)} later";
        header += "]";

        if (msg.ReplyTo is not null)
            header += $" (→ {SenderName(msg.ReplyTo)})";

        if (isTrigger)
            header += " (you're answering this)";

        return header;
    }

    private static string SenderName(Message msg) =>
        msg.SenderUser?.DisplayName ?? msg.SenderBot?.Name ?? "Unknown";

    private static string Describe(TimeSpan gap) => gap switch
    {
        { TotalHours: < 1 } => $"{Math.Round(gap.TotalMinutes)} min",
        { TotalDays: < 2 } => $"{Math.Round(gap.TotalHours)}h",
        _ => $"{Math.Round(gap.TotalDays)} days",
    };
}
