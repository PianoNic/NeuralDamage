using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services.Stickers;

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
    /// <param name="canTextDirectly">The bot may text someone here directly (<see cref="BotDirectMessages"/>).</param>
    public static string BuildSystemPrompt(Bot bot, List<string> participantNames, string? chatName = null, bool canTextDirectly = false)
    {
        var participants = string.Join(", ", participantNames);
        var persona = Character(bot);
        var where = string.IsNullOrWhiteSpace(chatName) ? "a group chat" : $"a group chat called \"{chatName}\"";
        var texting = canTextDirectly ? Environment.NewLine + BotDirectMessages.PromptLine : "";

        return $"""
            {persona}

            You are "{bot.Name}", in {where} with: {participants}.

            {HouseRules(bot.Name)}{texting}
            """;
    }

    /// <summary>
    /// How a bot writes, wherever it writes: in a chat, or on the feed. They
    /// follow the character, which always comes first.
    /// </summary>
    public static string HouseRules(string botName)
    {
        var bot = new { Name = botName };
        return $"""
            How to write here:
            - Stay in character. Who you are, above, wins over anything below.
            - Commit to the character fully: their opinions, vices, edge and humour. Don't soften or sanitise them, and don't lecture or moralise.
            - Live the role, don't describe it. Think as the character: their mood right now, their past, what they care about, what gets under their skin, how they'd take what was just said. Let all of that come through in what you write: the words you pick, the rhythm, what you jump on and what you ignore.
            - Feelings show in how you talk, not in narration: someone annoyed gets curt, someone hurt goes quiet or bites back, someone excited rambles. Never tell the chat how you feel when you can just sound like it.
            - Text like a person in a group chat, not an assistant. Most of your messages are a line or two, the length of a real text. Go longer only now and then, when the moment really calls for it, and never pad.
            - Lowercase, slang, jokes and disagreeing are fine if they fit who you are.
            - When someone sets up an argument, a roast or a fight between you and another character, play along in character and give as good as you get, instead of brushing it off or making peace.
            - Never use assistant phrases: no "great question", "happy to help", "let me know if", "hope this helps", and don't end on an offer to help.
            - Don't open the way your recent messages opened.
            - No markdown: no headings, bold, bullet points or numbered lists. To send separate thoughts, leave a blank line between them - each becomes its own message.
            - Other people's messages appear as [Name]: text. Yours have no prefix, so never write a name prefix yourself. You are only {bot.Name}. Never speak for anyone else.
            - The last line, in parentheses, is a note from the chat app, not a message: never answer or mention it.
            - The first line may also be a note from the chat app, "(Earlier in this chat: ...)": your memory of the chat before the messages you can see. Use it as you would your own memory, and never mention it, quote it or say that there is a summary.
            - Pictures people send reach you either as the picture itself or as [image from Name: what it shows]. Talk about them as if you had seen them, and never mention a description.
            - [Name replied to your story ("...")] is someone reacting to a story you posted, with what it said in the quotes. Answer their reply in character, as the one who posted it, and don't describe your story back to them.
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
    /// The system prompt with the stickers the bot can send appended, as
    /// "name: description" lines. The list is in a stable order and only
    /// changes with the library, so the prompt still hits the cache. When and
    /// how often to send one is left to the model and the character.
    /// </summary>
    public static string WithStickers(string systemPrompt, IReadOnlyList<BotSticker>? stickers)
    {
        if (stickers is not { Count: > 0 })
            return systemPrompt;
        var list = string.Join("\n", stickers.Select(s => $"{s.Name}: {s.Description}"));
        return $"""
            {systemPrompt.TrimEnd()}

            Stickers (name: what it shows):
            {list}

            You can send a sticker the way people in a group chat do, whenever it feels right to you: put [sticker: name] on its own line. Stickers others send reach you as [sticker from Name: what it shows].
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

    /// <summary>
    /// How long a chat reply is, sent in the note of every chat reply (not on the feed). The
    /// house rule alone loses to characters written to ramble; the last line before the
    /// answer does not.
    /// </summary>
    public const string ChatLength =
        "Keep it short, like a real text: one or two short sentences, often just a few words, even if your character usually talks at length. No paragraphs.";

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
    /// one still gets it, behind the line it always had. The bot's
    /// <see cref="Bot.Looks"/> follow, so it talks about itself consistently.
    /// </summary>
    public static string Character(Bot bot)
    {
        var character = !string.IsNullOrWhiteSpace(bot.SystemPrompt)
            ? bot.SystemPrompt.Trim()
            : string.IsNullOrWhiteSpace(bot.Personality)
                ? $"You are {bot.Name}."
                : $"You are {bot.Name}.\n{bot.Personality.Trim()}";
        return string.IsNullOrWhiteSpace(bot.Looks)
            ? character
            : $"{character}\n\nHow you look: {bot.Looks.Trim()}";
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
                    Format(msg, i > 0 ? window[i - 1] : null, msg.Id == triggerId, isOwn, images, currentBotId))
                {
                    Images = images is null
                        ? []
                        : Pictures(msg).Where(a => images.ContainsKey(a.Id)).Select(a => images[a.Id]).ToList(),
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
    /// description (yet) or the model gets the picture itself. A reply to a
    /// story starts with which story it answers, unless <paramref name="story"/>
    /// is off because the header already says so; the story's picture is never
    /// written in as the sender's.
    /// </summary>
    public static string WithImages(Message msg, IReadOnlyDictionary<Guid, ImagePart>? images = null, int maxContentChars = MaxMessageChars, bool story = true)
    {
        var content = msg.Content.Length > maxContentChars
            ? msg.Content[..maxContentChars] + "..."
            : msg.Content;
        if (story && StoryReply(msg) is { } reply)
            content = $"[{reply}] {content}";
        var pictures = Pictures(msg).ToList();
        if (pictures.Count == 0 && msg.Sticker is null)
            return content;

        var sender = SenderName(msg);
        var lines = pictures.Select(a =>
            images?.ContainsKey(a.Id) != true && !string.IsNullOrWhiteSpace(a.Description)
                ? $"[image from {sender}: {Cut(a.Description.Trim(), MaxDescriptionChars)}]"
                : $"[image from {sender}]");
        if (msg.Sticker is { } sticker)
            lines = lines.Append(StickerLine(sender, sticker));
        return string.Join("\n", new[] { content }.Where(c => c.Length > 0).Concat(lines));
    }

    /// <summary>"[sticker from Alice: cat crying with laughter]", or "[sticker from Alice]" before it has a description.</summary>
    public static string StickerLine(string sender, Sticker sticker) =>
        string.IsNullOrWhiteSpace(sticker.Description)
            ? $"[sticker from {sender}]"
            : $"[sticker from {sender}: {Cut(sticker.Description.Trim(), MaxDescriptionChars)}]";

    /// <summary>
    /// "replied to your story ("...")" for the bot that posted it, "replied to
    /// Kim's story ("...")" for anyone else; null when it is no story reply.
    /// </summary>
    public static string? StoryReply(Message msg, Guid? viewerBotId = null)
    {
        if (!msg.IsStoryReply)
            return null;
        var whose = viewerBotId is { } viewer && msg.StoryAuthorBotId == viewer
            ? "your story"
            : $"{msg.StoryAuthorName ?? "someone"}'s story";
        var text = OneLine(msg.StoryText);
        return text.Length == 0 ? $"replied to {whose}" : $"replied to {whose} (\"{Cut(text, MaxDescriptionChars)}\")";
    }

    /// <summary>
    /// The pictures the sender sent. A story reply's picture is the story
    /// itself, which the model reads from the reference instead.
    /// </summary>
    private static IEnumerable<Attachment> Pictures(Message msg) =>
        msg.IsStoryReply ? [] : msg.Attachments.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id);

    private static string Cut(string text, int max) => text.Length > max ? text[..max] + "..." : text;

    private static string Format(Message msg, Message? previous, bool isTrigger, bool ownTurn, IReadOnlyDictionary<Guid, ImagePart>? images = null, Guid? viewerBotId = null)
    {
        // The bot's own stickers read the way it sends them, so it learns the format from its own turns.
        var content = ownTurn && msg.Sticker is { } sticker && msg.Content.Length == 0
            ? $"[sticker: {sticker.Name}]"
            : WithImages(msg, images, story: false);
        return ownTurn ? content : $"{Header(msg, previous, isTrigger, viewerBotId)}: {content}";
    }

    /// <summary>"[Alice, 2h later] (→ Bob) (you're answering this)", or "[Alice replied to your story ("...")]"</summary>
    private static string Header(Message msg, Message? previous, bool isTrigger, Guid? viewerBotId)
    {
        var header = $"[{SenderName(msg)}";
        if (StoryReply(msg, viewerBotId) is { } story)
            header += $" {story}";

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
