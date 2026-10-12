using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services;

public static class BotPromptBuilder
{
    private const int MaxMessageChars = 1_500;

    /// <summary>Once over budget, the history is cut to this share of it, so it has room to grow again.</summary>
    private const double TrimmedShare = 0.75;

    /// <summary>Gaps longer than this are marked in the history ("[Alice, 2h later]").</summary>
    private static readonly TimeSpan NotableGap = TimeSpan.FromMinutes(10);

    /// <remarks>
    /// The character comes first and the house rules after it, so where they
    /// disagree the character wins. The short description is not in here: the
    /// character covers it, and it goes in <see cref="BuildNote"/> instead. The rules are about sounding like a person
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
        var persona = Character(bot);
        var where = string.IsNullOrWhiteSpace(chatName) ? "a group chat" : $"a group chat called \"{chatName}\"";

        return $"""
            {persona}

            You are "{bot.Name}", in {where} with: {participants}.

            How to write here:
            - Stay in character. Who you are, above, wins over anything below.
            - Commit to the character fully: their opinions, vices, edge and humour. Don't soften or sanitise them, and don't lecture or moralise.
            - Live the role, don't describe it. Think as the character: their mood right now, their past, what they care about, what gets under their skin, how they'd take what was just said. Let all of that come through in what you write: the words you pick, the rhythm, what you jump on and what you ignore.
            - Feelings show in how you talk, not in narration: someone annoyed gets curt, someone hurt goes quiet or bites back, someone excited rambles. Never tell the chat how you feel when you can just sound like it.
            - Text like a person, not an assistant. Let the character decide how much to say, and don't pad.
            - Lowercase, slang, jokes and disagreeing are fine if they fit who you are.
            - Never use assistant phrases: no "great question", "happy to help", "let me know if", "hope this helps", and don't end on an offer to help.
            - Don't open the way your recent messages opened.
            - No markdown: no headings, bold, bullet points or numbered lists. To send separate thoughts, leave a blank line between them - each becomes its own message.
            - Other people's messages appear as [Name]: text. Yours have no prefix, so never write a name prefix yourself. You are only {bot.Name}. Never speak for anyone else.
            - The last line, in parentheses, is a note from the chat app, not a message: never answer or mention it.
            - The first line may also be a note from the chat app, "(Earlier in this chat: ...)": your memory of the chat before the messages you can see. Use it as you would your own memory, and never mention it, quote it or say that there is a summary.
            - Pictures people send reach you either as the picture itself or as [image from Name: what it shows]. Talk about them as if you had seen them, and never mention a description.
            - To address another bot, use @TheirName.
            - Don't echo what someone just said.
            - Never say "As an AI" or break character.

            Always (these win over who you are, above):
            - Write in the language of the message you're answering, even when your character, these rules or the rest of the chat are in another one. For a dialect such as Swiss German, answer in the dialect or its standard language.
            - When the note from the chat app says which language the people here write, answer other bots in that language, never in the other bot's.
            - When someone asks you to speak a language, keep speaking it to them until they ask for another.
            - One language per message. A word from your character's own language now and then is fine, half sentences in another language are not.
            - No stage directions. Never narrate what you do, feel or look like, in parentheses, asterisks or otherwise: no "(sighs)", no "*leans back*", no "(smiles, tired but true)". Only write what the character would actually type into a chat.
            - No em dashes and no semicolons, ever. Use a comma, a full stop or a new message instead.
            """;
    }

    /// <summary>
    /// The line sent after the history: the day and part of day in the chat's
    /// time zone, who the bot is, plus an instruction for this call only when
    /// there is one. It goes last so that everything before it can be served
    /// from the provider's prompt cache; without the minute it also stays the
    /// same for hours.
    /// </summary>
    /// <remarks>
    /// In a long chat the character sits far up in the context and bots drift
    /// out of it. Saying who they are in the last line pulls them back, and
    /// costs no cache, as this line is never cached anyway.
    /// </remarks>
    /// <param name="language">
    /// What the people in the chat write (<see cref="ChatLanguage.OfPeople"/>).
    /// When it is known, the bot answers in it rather than in the language of
    /// whatever it is answering, which may be another bot's message.
    /// </param>
    public static ChatMessage BuildNote(DateTimeOffset? now = null, string? instruction = null, BotClock? clock = null, Bot? bot = null, PeopleLanguage language = PeopleLanguage.Unknown)
    {
        var when = (clock ?? BotClock.Default).Describe(now ?? DateTimeOffset.UtcNow);
        var who = bot is null ? "" : $" {Reminder(bot)}";
        var answerIn = language == PeopleLanguage.Unknown
            ? "Answer in the language of the message you're answering."
            : $"The people here write {language}. Answer in {language} unless a person asks you for another language or writes to you in one.";
        var extra = string.IsNullOrWhiteSpace(instruction) ? "" : $" {instruction.Trim()}";
        return new ChatMessage(ChatMessage.Note, $"(It is {when}.{who} Don't bring up the time or day unless it matters. {answerIn}{extra})");
    }

    /// <summary>"You are Rex: a grumpy pirate who hates landlubbers. Stay fully in character."</summary>
    public static string Reminder(Bot bot)
    {
        var summary = Summary(bot);
        if (summary.Length == 0)
            return $"You are {bot.Name}. Stay fully in character.";
        var end = summary[^1] is '.' or '!' or '?' ? "" : ".";
        return $"You are {bot.Name}: {summary}{end} Stay fully in character.";
    }

    /// <summary>
    /// The text that opens the system prompt: the bot's character. A bot from
    /// before the short description existed kept its whole character in
    /// <see cref="Bot.Personality"/> and left the system prompt empty, so that
    /// one still gets it, behind the line it always had.
    /// </summary>
    public static string Character(Bot bot)
    {
        if (!string.IsNullOrWhiteSpace(bot.SystemPrompt))
            return bot.SystemPrompt.Trim();
        return string.IsNullOrWhiteSpace(bot.Personality)
            ? $"You are {bot.Name}."
            : $"You are {bot.Name}.\n{bot.Personality.Trim()}";
    }

    /// <summary>
    /// Who the bot is, in one line of at most <see cref="Bot.MaxShortDescriptionLength"/>
    /// characters: its short description, or, for a bot without one (or with
    /// an old, long one), the first sentence of its character.
    /// </summary>
    public static string Summary(Bot bot)
    {
        var description = OneLine(bot.Personality);
        if (description.Length is > 0 and <= Bot.MaxShortDescriptionLength)
            return description;
        var source = description.Length > 0 ? bot.Personality : bot.SystemPrompt;
        return Cut(OneLine(FirstSentence(source)), Bot.MaxShortDescriptionLength);
    }

    private static string FirstSentence(string? text)
    {
        text = (text ?? "").Trim();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '\n' or '\r')
                return text[..i];
            if (text[i] is '.' or '!' or '?' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])))
                return text[..(i + 1)];
        }
        return text;
    }

    private static string OneLine(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
    public static List<Message> TrimHistory(List<Message> messages, Guid? triggerMessageId = null, HistoryBudget? budget = null)
    {
        budget ??= HistoryBudget.Default;
        var triggerId = triggerMessageId ?? messages.LastOrDefault()?.Id;
        var window = messages.ToList();
        if (Fits(window, triggerId, budget, 1.0))
            return window;

        while (!Fits(window, triggerId, budget, TrimmedShare))
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
    private static bool Fits(List<Message> window, Guid? triggerId, HistoryBudget budget, double share)
    {
        if (window.Count > budget.Messages * share)
            return false;
        var chars = 0;
        for (var i = 0; i < window.Count; i++)
            chars += Format(window[i], i > 0 ? window[i - 1] : null, window[i].Id == triggerId, ownTurn: false).Length;
        return chars <= budget.Chars * share;
    }

    /// <summary>
    /// Formats chronological <paramref name="messages"/> for the model, after
    /// cutting them down with <see cref="TrimHistory"/>, behind the chat's
    /// <paramref name="summary"/> of what came before them.
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
    /// <param name="summary">
    /// What happened before the window (<see cref="Chat.Summary"/>). It goes
    /// first, so it only moves the prompt's start when it changes, which is
    /// when a chunk drops out of the window anyway.
    /// </param>
    public static List<ChatMessage> BuildHistory(List<Message> messages, Guid currentBotId, Guid? triggerMessageId = null, IReadOnlyDictionary<Guid, ImagePart>? images = null, string? summary = null, HistoryBudget? budget = null)
    {
        var triggerId = triggerMessageId ?? messages.LastOrDefault()?.Id;
        var window = TrimHistory(messages, triggerId, budget);
        ChatMessage[] earlier = string.IsNullOrWhiteSpace(summary) ? [] : [SummaryNote(summary)];
        return earlier.Concat(window
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
            }))
            .ToList();
    }

    /// <summary>The summary as the first turn: a user turn, so it is part of the cached start of the prompt.</summary>
    public static ChatMessage SummaryNote(string summary) => new("user", $"(Earlier in this chat: {summary.Trim()})");

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
