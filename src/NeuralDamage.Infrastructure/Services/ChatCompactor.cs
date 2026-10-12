using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralDamage.Domain;
using NeuralDamage.Infrastructure.Services.Attachments;

namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// Keeps <see cref="Chat.Summary"/>: the messages that drop out of the bots'
/// history window are folded into it, so a chat can go on for ever and the
/// bots still remember what happened in it.
/// </summary>
public interface IChatCompactor
{
    /// <summary>
    /// Folds every message sent before <paramref name="before"/> into the
    /// summary, in the background. A chat already being compacted is left
    /// alone: the next trim asks again, from wherever the summary got to.
    /// </summary>
    /// <returns>The background run, for tests; callers do not wait for it.</returns>
    Task Schedule(Guid chatId, DateTime before);

    /// <summary>
    /// /compact: folds everything but the newest <see cref="HistoryOptions.CompactKeep"/>
    /// messages into the summary now, under the same per-chat lock as
    /// <see cref="Schedule"/>. Throws when the model fails, changing nothing.
    /// </summary>
    /// <returns>How many messages were folded: 0 when there was nothing to fold, null when the chat is already being compacted.</returns>
    Task<int?> CompactNowAsync(Guid chatId, CancellationToken ct = default);

    /// <summary>
    /// /summarize: writes a recap of the chat from its summary and the recent
    /// messages, in the background, and sends it to <paramref name="userId"/>
    /// alone, or tells them it failed. The recap is never stored.
    /// </summary>
    /// <returns>The background run, for tests; callers do not wait for it.</returns>
    Task Summarize(Guid chatId, Guid userId);
}

public class ChatCompactor(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ChatCompactor> logger,
    IOptions<HistoryOptions>? options = null) : IChatCompactor
{
    /// <summary>Low: a summary should be faithful, not creative.</summary>
    private const double Temperature = 0.3;

    /// <summary>The column's size (ChatConfiguration).</summary>
    public const int MaxStoredChars = 32_000;

    private readonly HistoryOptions _options = options?.Value ?? new();
    private readonly ConcurrentDictionary<Guid, byte> _running = new();

    public Task Schedule(Guid chatId, DateTime before)
    {
        if (!_running.TryAdd(chatId, 0))
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            try
            {
                await CompactAsync(chatId, before);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Compacting chat {ChatId} failed; the old summary stays", chatId);
            }
            finally
            {
                _running.TryRemove(chatId, out _);
            }
        });
    }

    public async Task<int?> CompactNowAsync(Guid chatId, CancellationToken ct = default)
    {
        if (!_running.TryAdd(chatId, 0))
            return null;
        try
        {
            DateTime before;
            int count;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
                var from = await db.Chats.Where(c => c.Id == chatId).Select(c => c.SummarizedBefore).FirstOrDefaultAsync(ct);
                var unsummarised = db.Messages.Where(m => m.ChatId == chatId);
                if (from is { } start)
                    unsummarised = unsummarised.Where(m => m.CreatedAt >= start);

                // Everything sent before the oldest message kept is folded.
                var oldestKept = await unsummarised.OrderByDescending(m => m.CreatedAt)
                    .Skip(Math.Max(1, _options.CompactKeep) - 1)
                    .Select(m => (DateTime?)m.CreatedAt)
                    .FirstOrDefaultAsync(ct);
                if (oldestKept is null)
                    return 0;
                before = oldestKept.Value;
                count = await unsummarised.CountAsync(m => m.CreatedAt < before, ct);
                if (count == 0)
                    return 0;
            }

            return await CompactAsync(chatId, before, ct) ? count : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Compacting chat {ChatId} on request failed; the old summary stays", chatId);
            throw;
        }
        finally
        {
            _running.TryRemove(chatId, out _);
        }
    }

    public Task Summarize(Guid chatId, Guid userId) => Task.Run(async () =>
    {
        using var scope = scopes.CreateScope();
        string text;
        try
        {
            text = await RecapAsync(scope.ServiceProvider, chatId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Summarizing chat {ChatId} failed", chatId);
            text = "Couldn't summarize the chat: the summary model did not answer. Try again in a moment.";
        }
        try
        {
            await scope.ServiceProvider.GetRequiredService<IChatNotificationService>().NotifyUserSystemMessage(chatId, userId, text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sending the recap of chat {ChatId} failed", chatId);
        }
    });

    /// <summary>The recap: the stored summary and the messages after it, retold for someone catching up.</summary>
    private async Task<string> RecapAsync(IServiceProvider services, Guid chatId)
    {
        var db = services.GetRequiredService<NeuralDamageDbContext>();
        var openRouter = services.GetRequiredService<IOpenRouterService>();

        var chat = await db.Chats.AsNoTracking().Where(c => c.Id == chatId)
            .Select(c => new SummarySeen(c.Summary, c.SummarizedBefore))
            .FirstOrDefaultAsync() ?? new SummarySeen(null, null);
        var query = db.Messages.Where(m => m.ChatId == chatId);
        if (chat.Before is { } start)
            query = query.Where(m => m.CreatedAt >= start);
        var lines = await NewestLinesAsync(query, CancellationToken.None);
        if (lines.Count == 0 && string.IsNullOrWhiteSpace(chat.Text))
            return "Nothing to summarize yet.";

        var earlier = string.IsNullOrWhiteSpace(chat.Text) ? "(none)" : chat.Text;
        var recent = lines.Count == 0 ? "(none)" : string.Join("\n", lines);
        var recap = Clean(await openRouter.GenerateResponseAsync(Model(), Temperature, RecapPrompt,
            [new ChatMessage("user", $"Summary of the earlier chat:\n{earlier}\n\nThe messages after it:\n{recent}")]));
        if (recap.Length == 0)
            throw new InvalidOperationException("The summary model returned an empty recap.");
        return "Summary of the chat so far:\n" + recap;
    }

    /// <summary>
    /// The newest messages of <paramref name="query"/> that fit <see cref="HistoryOptions.MaxFoldChars"/>,
    /// as "[Name]: text" lines, oldest first.
    /// </summary>
    private async Task<List<string>> NewestLinesAsync(IQueryable<Message> query, CancellationToken ct)
    {
        var newest = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(Math.Max(1, _options.MaxFoldChars / 20))
            .Include(m => m.SenderUser)
            .Include(m => m.SenderBot)
            .Include(m => m.Attachments)
            .AsNoTracking()
            .ToListAsync(ct);

        var lines = new List<string>();
        var chars = 0;
        foreach (var message in newest)
        {
            var line = Line(message);
            if (lines.Count > 0 && chars + line.Length > _options.MaxFoldChars)
                break;
            lines.Add(line);
            chars += line.Length + 1;
        }
        lines.Reverse();
        return lines;
    }

    /// <summary>
    /// The fold itself. Throws when the model fails, and then changes nothing:
    /// the old summary and where it ends stay as they were.
    /// </summary>
    /// <returns>Whether the summary was saved: not when the chat moved on (cleared, or compacted from elsewhere) meanwhile.</returns>
    public async Task<bool> CompactAsync(Guid chatId, DateTime before, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
        var openRouter = scope.ServiceProvider.GetRequiredService<IOpenRouterService>();

        var chat = await db.Chats.FirstOrDefaultAsync(c => c.Id == chatId, ct);
        if (chat is null || chat.SummarizedBefore >= before)
            return false;
        var from = chat.SummarizedBefore;

        var query = db.Messages.Where(m => m.ChatId == chatId && m.CreatedAt < before);
        if (from is { } start)
            query = query.Where(m => m.CreatedAt >= start);
        var lines = await NewestLinesAsync(query, ct);

        var summary = chat.Summary;
        if (lines.Count > 0)
        {
            var model = Model();
            var maxChars = Math.Clamp(_options.SummaryMaxChars, 200, MaxStoredChars);
            summary = Clean(await openRouter.GenerateResponseAsync(model, Temperature, FoldPrompt(maxChars),
                [new ChatMessage("user", $"Summary so far:\n{(string.IsNullOrWhiteSpace(chat.Summary) ? "(none yet)" : chat.Summary)}\n\nThe messages after it:\n{string.Join("\n", lines)}")], ct));
            if (summary.Length == 0)
                throw new InvalidOperationException($"{model} returned an empty summary.");

            if (summary.Length > maxChars)
            {
                logger.LogInformation("Summary of chat {ChatId} is {Length} characters, over {Max}; compacting it again", chatId, summary.Length, maxChars);
                var shorter = Clean(await openRouter.GenerateResponseAsync(model, Temperature, ShortenPrompt(maxChars), [new ChatMessage("user", summary)], ct));
                summary = Cut(shorter.Length > 0 && shorter.Length < summary.Length ? shorter : summary, maxChars);
            }
        }

        // The chat may have been cleared, or compacted from elsewhere, while
        // the model was writing: then this summary is of messages that are gone.
        var current = await db.Chats.AsNoTracking().Where(c => c.Id == chatId).Select(c => new { c.SummarizedBefore }).FirstOrDefaultAsync(ct);
        if (current is null || current.SummarizedBefore != from
            || !await db.Messages.AnyAsync(m => m.ChatId == chatId && m.CreatedAt < before, ct))
            return false;

        chat.Summary = summary;
        chat.SummarizedBefore = before;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Folded {Count} messages into the summary of chat {ChatId} ({Length} characters)", lines.Count, chatId, summary?.Length ?? 0);
        return true;
    }

    /// <summary><c>History:SummaryModel</c>, then <c>App:DefaultModel</c>, then the cheap model images are described with.</summary>
    private string Model() =>
        NullIfBlank(_options.SummaryModel) ?? NullIfBlank(configuration["App:DefaultModel"]) ?? ImageDescriber.DefaultModel;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Line(Message message)
    {
        var sender = message.SenderUser?.DisplayName ?? message.SenderBot?.Name ?? "Unknown";
        var kind = message.SenderBotId is null ? "" : " (bot)";
        return $"[{sender}{kind}]: {BotPromptBuilder.WithImages(message)}";
    }

    public static string FoldPrompt(int maxChars) => $"""
        You keep the memory of a group chat between people and AI characters ("bots").
        You get the summary so far and the messages that came after it. Write the new summary: the old one with the new messages folded in.

        Keep what the people in the chat would remember:
        - who said or did what, by name
        - facts people shared about themselves: names, jobs, plans, likes, dislikes, what is going on in their lives
        - running jokes, nicknames and catchphrases
        - arguments and questions that are still open, and promises made
        - how the people and bots get on with each other

        Write it neutrally, in the third person, so that every bot in the chat can read it as its own memory. Plain sentences, no headings, no markdown, no lists.
        Write it in the language most of the chat is in.
        Stay under {maxChars} characters: merge, and drop what matters least, oldest small talk first.
        Reply with the summary only.
        """;

    public const string RecapPrompt = """
        Someone in a group chat between people and AI characters ("bots") was away and wants to catch up.
        You get the summary of the earlier chat and the messages after it. Write them a short recap of the chat so far:
        who is in it and who said what, what is going on right now, arguments and questions still open, running jokes.

        Most space for what is going on now. Name people and bots by name. Plain sentences, no headings, no markdown, no lists.
        Write it in the language most of the chat is in. Stay under 1200 characters.
        Reply with the recap only.
        """;

    public static string ShortenPrompt(int maxChars) => $"""
        This is the memory of a group chat. It is too long. Rewrite it in under {maxChars} characters.
        Keep names, facts people shared about themselves, running jokes, nicknames and open arguments; drop what matters least.
        Plain sentences, no markdown, same language. Reply with the summary only.
        """;

    /// <summary>The summary without a wrapper the model may copy from the prompt, or markdown emphasis.</summary>
    private static string Clean(string? text)
    {
        var summary = (text ?? "").Trim();
        foreach (var prefix in new[] { "(Earlier in this chat:", "Summary so far:", "Summary:" })
            if (summary.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                summary = summary[prefix.Length..].Trim();
                if (prefix.StartsWith('(') && summary.EndsWith(')'))
                    summary = summary[..^1].Trim();
            }
        return summary.Replace("**", "");
    }

    /// <summary>Cut at the last sentence that fits, or hard at the cap when none does.</summary>
    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
            return text;
        var cut = text[..max];
        var end = cut.LastIndexOfAny(['.', '!', '?']);
        return end > max / 2 ? cut[..(end + 1)] : cut;
    }
}
