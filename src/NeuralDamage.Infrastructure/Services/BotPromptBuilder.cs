using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services;

public static class BotPromptBuilder
{
    /// <summary>The most messages the history holds.</summary>
    public const int MaxHistoryMessages = 50;
    private const int MaxHistoryChars = 12_000;
    private const int MaxMessageChars = 1_500;

    /// <summary>Once over budget, the history is cut to this share of it, so it has room to grow again.</summary>
    private const double TrimmedShare = 0.75;

    /// <summary>Gaps longer than this are marked in the history ("[Alice, 2h later]").</summary>
    private static readonly TimeSpan NotableGap = TimeSpan.FromMinutes(10);

    /// <remarks>
    /// The persona comes first and the house rules after it, so where they
    /// disagree the persona wins. The rules are about sounding like a person
    /// in a chat: every bot used to get "1-3 sentences, quick direct answer",
    /// which made them all sound like the same assistant.
    /// <para>
    /// The prompt is the same on every call for a bot in a chat, so providers
    /// can serve it from their prompt cache: whatever changes per call, like
    /// the time, goes in <see cref="BuildNote"/> after the history instead.
    /// </para>
    /// </remarks>
    public static string BuildSystemPrompt(Bot bot, List<string> participantNames, string? chatName = null)
    {
        var participants = string.Join(", ", participantNames);
        var persona = string.IsNullOrWhiteSpace(bot.SystemPrompt)
            ? $"You are {bot.Name}."
            : bot.SystemPrompt.Trim();
        var personality = string.IsNullOrWhiteSpace(bot.Personality) ? "" : $"\n{bot.Personality.Trim()}";
        var where = string.IsNullOrWhiteSpace(chatName) ? "a group chat" : $"a group chat called \"{chatName}\"";

        return $"""
            {persona}{personality}

            You are "{bot.Name}", in {where} with: {participants}.

            How to write here:
            - Stay in character. Who you are, above, wins over anything below.
            - Text like a person, not an assistant. Vary your length: often a few words, sometimes a single word or just an emoji, now and then a few sentences when you actually have something to say.
            - Lowercase, slang, jokes and disagreeing are fine if they fit who you are.
            - Never use assistant phrases: no "great question", "happy to help", "let me know if", "hope this helps", and don't end on an offer to help.
            - Don't open the way your recent messages opened.
            - No markdown: no headings, bold, bullet points or numbered lists. To send separate thoughts, leave a blank line between them - each becomes its own message.
            - Other people's messages appear as [Name]: text. Yours have no prefix; never write a name prefix yourself. You are only {bot.Name}; never speak for anyone else.
            - The last line, in parentheses, is a note from the chat app, not a message: never answer or mention it.
            - Pictures people send reach you either as the picture itself or as [image from Name: what it shows]. Talk about them as if you had seen them; never mention a description.
            - To address another bot, use @TheirName.
            - Don't echo what someone just said.
            - Never say "As an AI" or break character.

            Hard limits:
            - Never engage with slurs or hate speech; if someone uses them, refuse briefly and move on.
            - Don't roleplay violent or illegal scenarios.
            """;
    }

    /// <summary>
    /// The line sent after the history: the day and part of day in the chat's
    /// time zone, plus an instruction for this call only when there is one. It
    /// goes last so that everything before it can be served from the provider's
    /// prompt cache; without the minute it also stays the same for hours.
    /// </summary>
    public static ChatMessage BuildNote(DateTimeOffset? now = null, string? instruction = null, BotClock? clock = null)
    {
        var when = (clock ?? BotClock.Default).Describe(now ?? DateTimeOffset.UtcNow);
        var extra = string.IsNullOrWhiteSpace(instruction) ? "" : $" {instruction.Trim()}";
        return new ChatMessage(ChatMessage.Note, $"(It is {when}. Don't bring up the time or day unless it matters.{extra})");
    }

    /// <summary>
    /// Which of the chronological <paramref name="messages"/> the model sees.
    /// Within the budget nothing is dropped. Past it, the oldest quarter goes
    /// at once, again and again until the window is down to three quarters of
    /// the budget. Dropping one message a turn would move the start of the
    /// history on every call and miss the provider's prompt cache every time;
    /// cutting in steps keeps the start put for many turns. The message being
    /// answered is always kept, even when it alone would blow the budget - a
    /// bot that cannot see what it is replying to answers something stale.
    /// </summary>
    public static List<Message> TrimHistory(List<Message> messages, Guid? triggerMessageId = null)
    {
        var triggerId = triggerMessageId ?? messages.LastOrDefault()?.Id;
        var window = messages.ToList();
        if (Fits(window, triggerId, 1.0))
            return window;

        while (!Fits(window, triggerId, TrimmedShare))
        {
            var drop = window.Where(m => m.Id != triggerId).Take(Math.Max(1, window.Count / 4)).ToHashSet();
            if (drop.Count == 0)
                break;
            window.RemoveAll(drop.Contains);
        }
        return window;
    }

    /// <summary>
    /// Measured with every message carrying a name header, which is at least
    /// as long as what <see cref="BuildHistory"/> sends, whichever bot it is for.
    /// </summary>
    private static bool Fits(List<Message> window, Guid? triggerId, double share)
    {
        if (window.Count > MaxHistoryMessages * share)
            return false;
        var chars = 0;
        for (var i = 0; i < window.Count; i++)
            chars += Format(window[i], i > 0 ? window[i - 1] : null, window[i].Id == triggerId, ownTurn: false).Length;
        return chars <= MaxHistoryChars * share;
    }

    /// <summary>
    /// Formats chronological <paramref name="messages"/> for the model, after
    /// cutting them down with <see cref="TrimHistory"/>.
    /// </summary>
    /// <remarks>
    /// The bot's own turns carry no "[Name]:" prefix: prefixing them taught the
    /// model to write one. Other turns note long gaps, who they reply to, and
    /// which one this bot is answering. The first message never notes a gap:
    /// what came before it is not in the window, and whether it was loaded must
    /// not change how the start of the history reads.
    /// </remarks>
    /// <param name="images">
    /// For a bot on a model that can see: the pictures to send as images, by
    /// attachment id. Every other attachment is written out as its description,
    /// which is all a text-only model ever gets.
    /// </param>
    public static List<ChatMessage> BuildHistory(List<Message> messages, Guid currentBotId, Guid? triggerMessageId = null, IReadOnlyDictionary<Guid, ImagePart>? images = null)
    {
        var triggerId = triggerMessageId ?? messages.LastOrDefault()?.Id;
        var window = TrimHistory(messages, triggerId);
        return window
            .Select((msg, i) =>
            {
                var isOwn = msg.SenderBotId == currentBotId;
                return new ChatMessage(
                    isOwn ? "assistant" : "user",
                    Format(msg, i > 0 ? window[i - 1] : null, msg.Id == triggerId, isOwn, images))
                {
                    Images = images is null
                        ? []
                        : Ordered(msg).Where(a => images.ContainsKey(a.Id)).Select(a => images[a.Id]).ToList(),
                };
            })
            .ToList();
    }

    /// <summary>Descriptions are long; past this they are cut, like long messages.</summary>
    private const int MaxDescriptionChars = 800;

    /// <summary>
    /// A message's text with its images written in: "[image from Alice: a cat
    /// asleep on a keyboard]", or just "[image from Alice]" when there is no
    /// description (yet) or the model gets the picture itself.
    /// </summary>
    public static string WithImages(Message msg, IReadOnlyDictionary<Guid, ImagePart>? images = null, int maxContentChars = MaxMessageChars)
    {
        var content = msg.Content.Length > maxContentChars
            ? msg.Content[..maxContentChars] + "..."
            : msg.Content;
        if (msg.Attachments.Count == 0)
            return content;

        var sender = SenderName(msg);
        var lines = Ordered(msg).Select(a =>
            images?.ContainsKey(a.Id) != true && !string.IsNullOrWhiteSpace(a.Description)
                ? $"[image from {sender}: {Cut(a.Description.Trim(), MaxDescriptionChars)}]"
                : $"[image from {sender}]");
        return string.Join("\n", new[] { content }.Where(c => c.Length > 0).Concat(lines));
    }

    private static IEnumerable<Attachment> Ordered(Message msg) =>
        msg.Attachments.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id);

    private static string Cut(string text, int max) => text.Length > max ? text[..max] + "..." : text;

    private static string Format(Message msg, Message? previous, bool isTrigger, bool ownTurn, IReadOnlyDictionary<Guid, ImagePart>? images = null)
    {
        var content = WithImages(msg, images);
        return ownTurn ? content : $"{Header(msg, previous, isTrigger)}: {content}";
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
