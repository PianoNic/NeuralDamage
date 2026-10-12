namespace NeuralDamage.Infrastructure.Services;

/// <summary>
/// Per-chat switches set by the /stop and /mute commands that keep bots quiet.
/// </summary>
public interface IChatBotState
{
    /// <summary>Silences every bot in the chat until <see cref="Resume"/>.</summary>
    void Stop(Guid chatId);
    void Resume(Guid chatId);
    bool IsStopped(Guid chatId);

    void Mute(Guid chatId, Guid botId);
    void Unmute(Guid chatId, Guid botId);
    bool IsMuted(Guid chatId, Guid botId);

    /// <summary>
    /// True the first time it is asked for a chat and bot, so the notice that
    /// the bot's model is gone is posted once rather than on every message.
    /// </summary>
    bool TryMarkModelNotice(Guid chatId, Guid botId);
    /// <summary>Lets the notice show again, in every chat, once the bot's model is changed.</summary>
    void ClearModelNotices(Guid botId);

    /// <summary>
    /// When the oldest message in the bot's last history window was sent, so
    /// the next reply starts its history there too and the prompt keeps the
    /// same beginning (see <see cref="BotPromptBuilder.TrimHistory"/>).
    /// </summary>
    DateTime? HistoryStart(Guid chatId, Guid botId);
    void SetHistoryStart(Guid chatId, Guid botId, DateTime start);

    /// <summary>
    /// The chat summary the bot's last history started with. The bot keeps it
    /// until its own window moves, so another bot's compaction does not change
    /// the start of its prompt and miss the cache.
    /// </summary>
    SummarySeen? HistorySummary(Guid chatId, Guid botId);
    void SetHistorySummary(Guid chatId, Guid botId, SummarySeen summary);
}

/// <summary>A <see cref="NeuralDamage.Domain.Chat.Summary"/> as read, with where it ends.</summary>
public record SummarySeen(string? Text, DateTime? Before);
