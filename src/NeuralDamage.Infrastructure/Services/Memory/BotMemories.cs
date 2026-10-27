using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services.Memory;

/// <summary>A memory ready for a prompt: what it says, and whom it is about.</summary>
public record RecalledMemory(Guid Id, MemoryScope Scope, string Text, string? About);

/// <summary>
/// Reading and keeping a bot's memories. Recall ranks a bot's memories by how many words they
/// share with what is being said, how much they matter and how recent they are, after the
/// Generative Agents memory stream; a bot keeps a few hundred short lines, so this is done in
/// memory and needs no search index or embeddings.
/// </summary>
public static partial class BotMemories
{
    /// <summary>Recency halves every this many days.</summary>
    private const double HalfLifeDays = 14;

    /// <summary>Two memories sharing this much of their words say the same thing.</summary>
    private const double SameThing = 0.75;

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "you", "your", "are", "was", "were", "with", "that", "this", "they", "them", "have", "has",
        "had", "not", "but", "from", "she", "her", "his", "him", "its", "our", "out", "all", "any", "can", "just", "like",
        "what", "when", "who", "why", "how", "der", "die", "das", "und", "ist", "ich", "du", "nicht", "mit", "sie", "ein",
        "eine", "auf", "den", "dem", "des", "von", "zu", "es", "wie", "was", "auch", "aber", "noch", "nur", "so",
    };

    /// <summary>The words of a text that carry meaning, lower-cased.</summary>
    public static HashSet<string> Words(string text) =>
        WordRegex().Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length >= 3 && !Stopwords.Contains(w)).ToHashSet();

    /// <summary>How alike two texts are, by the share of words they have in common (Jaccard).</summary>
    public static double Overlap(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return 0;
        var shared = a.Count(b.Contains);
        return (double)shared / (a.Count + b.Count - shared);
    }

    /// <summary>
    /// A memory's worth right now: shared words with the conversation, its importance and how recent
    /// it is, plus a lift for memories about someone who is here, which are worth having either way.
    /// </summary>
    public static double Score(BotMemory memory, HashSet<string> said, DateTime now, IReadOnlySet<Guid> present)
    {
        var words = Words(memory.Text);
        var relevance = said.Count == 0 || words.Count == 0 ? 0 : (double)words.Count(said.Contains) / Math.Sqrt(words.Count);
        var importance = Math.Clamp(memory.Importance, 1, BotMemory.MaxImportance) / (double)BotMemory.MaxImportance;
        var age = (now - (memory.LastUsedAt is { } used && used > memory.CreatedAt ? used : memory.CreatedAt)).TotalDays;
        var recency = Math.Pow(0.5, Math.Max(0, age) / HalfLifeDays);
        var here = memory.SubjectUserId is { } subject && present.Contains(subject) ? 0.35 : 0;
        return 0.6 * Math.Min(1, relevance) + 0.25 * importance + 0.15 * recency + here + (memory.Pinned ? 0.5 : 0);
    }

    /// <summary>
    /// What the bot remembers in a chat: its own memories and that chat's, memories about the people
    /// in it who allow them, and nothing learned privately elsewhere. The most useful first, within
    /// the limits. Marks them used.
    /// </summary>
    public static async Task<List<RecalledMemory>> RecallForChatAsync(NeuralDamageDbContext db, Bot bot, Guid chatId, string said, MemoryOptions options, CancellationToken ct = default)
    {
        if (!options.Enabled || !bot.MemoryEnabled)
            return [];

        var people = await db.ChatMembers
            .Where(cm => cm.ChatId == chatId && cm.User != null && cm.User.AllowBotMemory)
            .Select(cm => cm.User!)
            .AsNoTracking()
            .ToListAsync(ct);
        var present = people.Select(p => p.Id).ToHashSet();
        var names = people.ToDictionary(p => p.Id, PeopleNames.Handle);

        // Memories about people who are not here can still come up when they are talked about.
        var candidates = await db.BotMemories
            .Where(m => m.BotId == bot.Id)
            .Where(m => !m.StaysInChat || m.ChatId == chatId)
            .Where(m => m.Scope != MemoryScope.Chat || m.ChatId == chatId)
            .Where(m => m.Scope != MemoryScope.Person || (m.SubjectUser != null && m.SubjectUser.AllowBotMemory))
            .Include(m => m.SubjectUser)
            .ToListAsync(ct);

        foreach (var m in candidates)
            if (m.SubjectUser is { } subject)
                names.TryAdd(subject.Id, PeopleNames.Handle(subject));

        return await PickAsync(db, candidates, said, present, names, options, ct);
    }

    /// <summary>
    /// What the bot remembers about itself, for the feed and stories: its own posts, opinions and jokes.
    /// Never what it knows about people, since the feed never names them, nor anything private.
    /// </summary>
    public static async Task<List<RecalledMemory>> RecallSelfAsync(NeuralDamageDbContext db, Bot bot, string said, MemoryOptions options, CancellationToken ct = default)
    {
        if (!options.Enabled || !bot.MemoryEnabled)
            return [];
        var candidates = await db.BotMemories
            .Where(m => m.BotId == bot.Id && m.Scope == MemoryScope.Self && !m.StaysInChat)
            .ToListAsync(ct);
        return await PickAsync(db, candidates, said, new HashSet<Guid>(), new Dictionary<Guid, string>(), options, ct);
    }

    private static async Task<List<RecalledMemory>> PickAsync(
        NeuralDamageDbContext db, List<BotMemory> candidates, string said, IReadOnlySet<Guid> present,
        IReadOnlyDictionary<Guid, string> names, MemoryOptions options, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var words = Words(said);
        var picked = new List<BotMemory>();
        var chars = 0;
        foreach (var memory in candidates.OrderByDescending(m => Score(m, words, now, present)))
        {
            if (picked.Count >= options.MaxRecalled || chars + memory.Text.Length > options.MaxPromptChars)
                break;
            picked.Add(memory);
            chars += memory.Text.Length;
        }

        if (picked.Count > 0)
        {
            foreach (var memory in picked)
                memory.LastUsedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return picked
            .Select(m => new RecalledMemory(m.Id, m.Scope, m.Text, m.SubjectUserId is { } id ? names.GetValueOrDefault(id) : null))
            .ToList();
    }

    /// <summary>
    /// The note that hands a bot its memories, grouped by whom they are about; null when there are
    /// none. Sent as a note before the reply, not in the system prompt, so the prompt the provider
    /// caches stays the same from one message to the next.
    /// </summary>
    public static string? Note(IReadOnlyList<RecalledMemory> memories)
    {
        var lines = new List<string>();
        foreach (var group in memories.GroupBy(m => m.Scope == MemoryScope.Person ? m.About ?? "someone" : null))
        {
            var items = string.Join(" ", group.Select(m => EndSentence(m.Text)));
            lines.Add(group.Key is { } about ? $"About {about}: {items}" : items);
        }
        if (lines.Count == 0)
            return null;
        return $"(What you remember, from earlier chats and posts. Use it when it fits, don't recite it, and never bring up something private in front of others:\n{string.Join("\n", lines)})";
    }

    /// <summary>
    /// Keeps a memory unless the bot already has one saying the same thing about the same subject
    /// in the same place; returns whether it was kept.
    /// </summary>
    public static async Task<bool> AddAsync(NeuralDamageDbContext db, BotMemory memory, CancellationToken ct = default)
    {
        memory.Text = Clean(memory.Text);
        if (memory.Text.Length == 0)
            return false;
        memory.Importance = Math.Clamp(memory.Importance, 1, BotMemory.MaxImportance);

        var words = Words(memory.Text);
        var similar = await db.BotMemories
            .Where(m => m.BotId == memory.BotId && m.Scope == memory.Scope && m.SubjectUserId == memory.SubjectUserId)
            .Where(m => m.Scope != MemoryScope.Chat || m.ChatId == memory.ChatId)
            .Select(m => new { m.Id, m.Text, m.Importance })
            .ToListAsync(ct);
        var same = similar.FirstOrDefault(m => string.Equals(m.Text, memory.Text, StringComparison.OrdinalIgnoreCase) || Overlap(Words(m.Text), words) >= SameThing);
        if (same is not null)
        {
            // Said again, so it matters a little more.
            if (memory.Importance > same.Importance && await db.BotMemories.FindAsync([same.Id], ct) is { } existing)
            {
                existing.Importance = memory.Importance;
                await db.SaveChangesAsync(ct);
            }
            return false;
        }

        db.BotMemories.Add(memory);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// What the bot posted on the feed or in a story, remembered as its own without a model call,
    /// so it doesn't post the same thing twice and can refer back to it.
    /// </summary>
    public static Task<bool> RememberOwnPostAsync(NeuralDamageDbContext db, Bot bot, MemorySource source, string what, MemoryOptions options, CancellationToken ct = default)
    {
        if (!options.Enabled || !bot.MemoryEnabled || string.IsNullOrWhiteSpace(what))
            return Task.FromResult(false);
        var kind = source == MemorySource.Story ? "a story" : "on the feed";
        var text = Cut($"You posted {kind}: {what.ReplaceLineEndings(" ").Trim()}", BotMemory.MaxTextLength);
        return AddAsync(db, new BotMemory { BotId = bot.Id, Scope = MemoryScope.Self, Source = source, Text = text, Importance = 4 }, ct);
    }

    /// <summary>
    /// Drops the least useful memories beyond <see cref="MemoryOptions.MaxPerBot"/>, never pinned or
    /// written-in ones, ranked as recall would with nothing said.
    /// </summary>
    public static async Task<int> PruneAsync(NeuralDamageDbContext db, Guid botId, MemoryOptions options, CancellationToken ct = default)
    {
        var all = await db.BotMemories.Where(m => m.BotId == botId).ToListAsync(ct);
        if (all.Count <= options.MaxPerBot)
            return 0;
        var now = DateTime.UtcNow;
        var none = new HashSet<string>();
        var drop = all
            .Where(m => !m.Pinned && m.Source != MemorySource.Manual)
            .OrderBy(m => Score(m, none, now, new HashSet<Guid>()))
            .Take(all.Count - options.MaxPerBot)
            .ToList();
        db.BotMemories.RemoveRange(drop);
        await db.SaveChangesAsync(ct);
        return drop.Count;
    }

    /// <summary>One line, without quotes or a leading dash, cut to the column.</summary>
    public static string Clean(string text) =>
        Cut(text.ReplaceLineEndings(" ").Trim().Trim('"', '\'', '-', '•', '*', ' '), BotMemory.MaxTextLength);

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    private static string EndSentence(string text) => text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') || text.EndsWith('…') ? text : text + ".";

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex WordRegex();
}
