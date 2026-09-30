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
}
