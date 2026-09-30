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
}
