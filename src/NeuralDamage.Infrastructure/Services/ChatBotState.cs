using System.Collections.Concurrent;

namespace NeuralDamage.Infrastructure.Services;

/// <remarks>
/// Held in memory like the legacy server did, so every stop and mute resets
/// when the API restarts. Move it to the database if that starts to matter.
/// </remarks>
public class ChatBotState : IChatBotState
{
    private readonly ConcurrentDictionary<Guid, byte> _stopped = new();
    private readonly ConcurrentDictionary<(Guid ChatId, Guid BotId), byte> _muted = new();
    private readonly ConcurrentDictionary<(Guid ChatId, Guid BotId), byte> _modelNotices = new();

    public void Stop(Guid chatId) => _stopped[chatId] = 0;
    public void Resume(Guid chatId) => _stopped.TryRemove(chatId, out _);
    public bool IsStopped(Guid chatId) => _stopped.ContainsKey(chatId);

    public void Mute(Guid chatId, Guid botId) => _muted[(chatId, botId)] = 0;
    public void Unmute(Guid chatId, Guid botId) => _muted.TryRemove((chatId, botId), out _);
    public bool IsMuted(Guid chatId, Guid botId) => _muted.ContainsKey((chatId, botId));

    public bool TryMarkModelNotice(Guid chatId, Guid botId) => _modelNotices.TryAdd((chatId, botId), 0);
    public void ClearModelNotices(Guid botId)
    {
        foreach (var key in _modelNotices.Keys.Where(k => k.BotId == botId))
            _modelNotices.TryRemove(key, out _);
    }
}
