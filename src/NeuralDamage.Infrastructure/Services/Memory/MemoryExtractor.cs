using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Infrastructure.Services.Memory;

public interface IMemoryExtractor
{
    /// <summary>
    /// A bot wrote in a chat. Once it has written <see cref="MemoryOptions.ExtractEvery"/> messages
    /// there since its memory last read the chat, it reads the new part in the background.
    /// </summary>
    /// <returns>The background run, for tests; callers do not wait for it.</returns>
    Task BotSpoke(Guid chatId, Guid botId);
}

/// <summary>
/// Picks what is worth remembering out of a chat for one bot, with one call to a cheap model per
/// batch of its messages, the way Mem0 extracts facts: things about the people (that they allow),
/// the chat's in-jokes, and what the bot said about itself. What was said in private stays there.
/// </summary>
public class MemoryExtractor(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<MemoryExtractor> logger,
    IOptions<MemoryOptions>? options = null) : IMemoryExtractor
{
    /// <summary>Low: it should write down what was said, not invent.</summary>
    private const double Temperature = 0.2;

    /// <summary>The most new memories one read keeps.</summary>
    public const int MaxPerRead = 8;

    private readonly MemoryOptions _options = options?.Value ?? new();
    private readonly ConcurrentDictionary<(Guid Bot, Guid Chat), byte> _running = new();
    private readonly ConcurrentDictionary<(Guid Bot, DateOnly Day), int> _today = new();

    public Task BotSpoke(Guid chatId, Guid botId)
    {
        if (!_options.Enabled || !_running.TryAdd((botId, chatId), 0))
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            try
            {
                await ReadAsync(chatId, botId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reading chat {ChatId} into the memory of bot {BotId} failed", chatId, botId);
            }
            finally
            {
                _running.TryRemove((botId, chatId), out _);
            }
        });
    }

    /// <summary>Reads the chat for the bot when enough is new; returns how many memories it kept.</summary>
    public async Task<int> ReadAsync(Guid chatId, Guid botId, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();

        var bot = await db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == botId && b.IsActive, ct);
        var chat = await db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chatId, ct);
        if (bot is null || chat is null || !bot.MemoryEnabled)
            return 0;

        var cursor = await db.BotMemoryCursors.FirstOrDefaultAsync(c => c.BotId == botId && c.ChatId == chatId, ct);
        var since = cursor?.ProcessedBefore ?? DateTime.MinValue;
        var spoke = await db.Messages.CountAsync(m => m.ChatId == chatId && m.SenderBotId == botId && m.CreatedAt > since, ct);
        if (spoke < Math.Max(1, _options.ExtractEvery))
            return 0;

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_today.GetValueOrDefault((botId, day)) >= _options.MaxExtractionsPerBotPerDay)
        {
            logger.LogInformation("Bot {BotName} reached its memory reads for today", bot.Name);
            return 0;
        }

        var messages = await db.Messages
            .Where(m => m.ChatId == chatId && m.CreatedAt > since)
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Max(1, _options.ExtractWindow))
            .Include(m => m.SenderUser)
            .Include(m => m.SenderBot)
            .Include(m => m.Sticker)
            .AsNoTracking()
            .ToListAsync(ct);
        if (messages.Count == 0)
            return 0;
        messages.Reverse();

        var people = await db.ChatMembers
            .Where(cm => cm.ChatId == chatId && cm.User != null)
            .Select(cm => cm.User!)
            .AsNoTracking()
            .ToListAsync(ct);
        var known = await db.BotMemories
            .Where(m => m.BotId == botId && (m.ChatId == chatId || m.Scope != MemoryScope.Chat))
            .OrderByDescending(m => m.CreatedAt)
            .Take(40)
            .Select(m => m.Text)
            .ToListAsync(ct);

        var reply = await scope.ServiceProvider.GetRequiredService<IOpenRouterService>()
            .GenerateResponseAsync(Model(), Temperature, SystemPrompt(bot), [new ChatMessage("user", Transcript(bot, messages, people, known))], ct);

        _today.AddOrUpdate((botId, day), 1, (_, n) => n + 1);
        if (cursor is null)
            db.BotMemoryCursors.Add(new BotMemoryCursor { BotId = botId, ChatId = chatId, ProcessedBefore = messages[^1].CreatedAt });
        else
            cursor.ProcessedBefore = messages[^1].CreatedAt;
        await db.SaveChangesAsync(ct);

        var kept = 0;
        var inPrivate = chat.IsDirect || bot.ChatId is not null;
        foreach (var found in Parse(reply).Take(MaxPerRead))
        {
            if (ToMemory(found, bot, chat, people, inPrivate) is { } memory && await BotMemories.AddAsync(db, memory, ct))
                kept++;
        }
        if (kept > 0)
            logger.LogInformation("Bot {BotName} remembered {Count} things from chat {ChatId}", bot.Name, kept, chatId);
        return kept;
    }

    /// <summary>What the extracting model is asked.</summary>
    public static string SystemPrompt(Bot bot) => $$"""
        You keep the long-term memory of "{{bot.Name}}", a character in a group chat app. Read the chat and write down only what is worth remembering for weeks: facts about the people (what they like, their plans, pets, jobs, relationships, things that happened to them), the chat's running jokes, and what {{bot.Name}} said about itself, promised or decided.

        Skip greetings, small talk, anything already known, and anything only true for a moment. Never write down passwords, addresses, phone numbers or other private details. Don't invent: only what the chat says.

        Answer with a JSON array and nothing else, at most {{MaxPerRead}} items, an empty array when nothing is worth keeping:
        [{"about": "<a person's name exactly as written in the chat, or self, or chat>", "text": "<one short sentence, in the third person, e.g. Niclas has a cat called Olivia>", "importance": <1 to 10>}]
        """;

    /// <summary>The chat as the extracting model reads it, after what the bot already knows.</summary>
    public static string Transcript(Bot bot, IReadOnlyList<Message> messages, IReadOnlyList<User> people, IReadOnlyList<string> known)
    {
        var who = people.Count == 0 ? "" : $"People here: {string.Join(", ", people.Select(PeopleNames.ForPrompt))}.\n";
        var already = known.Count == 0 ? "" : $"Already remembered:\n{string.Join("\n", known.Select(k => $"- {k}"))}\n\n";
        var lines = messages.Select(m =>
        {
            var name = m.SenderBot?.Name ?? (m.SenderUser is { } user ? PeopleNames.Handle(user) : "Someone");
            var text = m.Content.Length > 0 ? m.Content.ReplaceLineEndings(" ") : m.Sticker is { } sticker ? $"(sticker: {sticker.Name})" : "(a picture)";
            return $"[{name}]: {(text.Length > 400 ? text[..400] + "…" : text)}";
        });
        return $"{who}{already}The chat, where {bot.Name} is one of the speakers:\n{string.Join("\n", lines)}";
    }

    /// <summary>One thing the model picked out.</summary>
    public record Found(string About, string Text, int Importance);

    /// <summary>The items of the model's JSON array; whatever it wrote around them is ignored.</summary>
    public static List<Found> Parse(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return [];
        var start = reply.IndexOf('[');
        var end = reply.LastIndexOf(']');
        if (start < 0 || end <= start)
            return [];
        try
        {
            using var json = JsonDocument.Parse(reply[start..(end + 1)]);
            var found = new List<Found>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var about = item.TryGetProperty("about", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "";
                var text = item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
                var importance = item.TryGetProperty("importance", out var i) && i.TryGetInt32(out var n) ? n : 5;
                if (text.Trim().Length > 0)
                    found.Add(new Found(about.Trim(), text, importance));
            }
            return found;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// A found item as a memory: about the bot, the chat, or a person here by name or nickname who
    /// allows memories; null for anyone else. Learned in private, it stays in this chat.
    /// </summary>
    public static BotMemory? ToMemory(Found found, Bot bot, Chat chat, IReadOnlyList<User> people, bool inPrivate)
    {
        var about = found.About.TrimStart('@');
        if (about.Equals("self", StringComparison.OrdinalIgnoreCase) || about.Equals(bot.Name, StringComparison.OrdinalIgnoreCase))
            return new BotMemory { BotId = bot.Id, Scope = MemoryScope.Self, ChatId = chat.Id, StaysInChat = inPrivate, Source = MemorySource.Chat, Text = found.Text, Importance = found.Importance };
        if (about.Equals("chat", StringComparison.OrdinalIgnoreCase))
            return new BotMemory { BotId = bot.Id, Scope = MemoryScope.Chat, ChatId = chat.Id, StaysInChat = true, Source = MemorySource.Chat, Text = found.Text, Importance = found.Importance };

        var person = people.FirstOrDefault(p =>
            string.Equals(PeopleNames.Handle(p), about, StringComparison.OrdinalIgnoreCase)
            || PeopleNames.Split(p.Nicknames).Any(n => string.Equals(n, about, StringComparison.OrdinalIgnoreCase)));
        if (person is null || !person.AllowBotMemory)
            return null;
        return new BotMemory { BotId = bot.Id, Scope = MemoryScope.Person, SubjectUserId = person.Id, ChatId = chat.Id, StaysInChat = inPrivate, Source = MemorySource.Chat, Text = found.Text, Importance = found.Importance };
    }

    /// <summary><c>Memory:Model</c>, then <c>History:SummaryModel</c>, then <c>App:DefaultModel</c>, then the cheap model images are described with.</summary>
    private string Model() =>
        NullIfBlank(_options.Model) ?? NullIfBlank(configuration["History:SummaryModel"]) ?? NullIfBlank(configuration["App:DefaultModel"]) ?? ImageDescriber.DefaultModel;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
