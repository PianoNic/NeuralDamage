using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NeuralDamage.Domain;

namespace NeuralDamage.Infrastructure.Services;

/// <remarks>
/// Stops and mutes are written to the database (<see cref="BotSilence"/>) so
/// they survive a restart, and read from memory, since every bot round asks.
/// The table is read once, on first use. Model notices and history starts only
/// save a repeated notice or a prompt cache miss, so they stay in memory.
/// Built without a scope factory, as the tests do, nothing is persisted.
/// </remarks>
public class ChatBotState : IChatBotState
{
    private readonly IServiceScopeFactory? _scopes;
    private readonly Lock _gate = new();
    private volatile bool _loaded;

    private readonly ConcurrentDictionary<Guid, byte> _stopped = new();
    private readonly ConcurrentDictionary<(Guid ChatId, Guid BotId), byte> _muted = new();
    private readonly ConcurrentDictionary<(Guid ChatId, Guid BotId), byte> _modelNotices = new();
    private readonly ConcurrentDictionary<(Guid ChatId, Guid BotId), DateTime> _historyStarts = new();

    public ChatBotState() => _loaded = true;

    public ChatBotState(IServiceScopeFactory scopes) => _scopes = scopes;

    public void Stop(Guid chatId) => Silence(chatId, null);
    public void Resume(Guid chatId) => Unsilence(chatId, null);
    public bool IsStopped(Guid chatId)
    {
        EnsureLoaded();
        return _stopped.ContainsKey(chatId);
    }

    public void Mute(Guid chatId, Guid botId) => Silence(chatId, botId);
    public void Unmute(Guid chatId, Guid botId) => Unsilence(chatId, botId);
    public bool IsMuted(Guid chatId, Guid botId)
    {
        EnsureLoaded();
        return _muted.ContainsKey((chatId, botId));
    }

    public bool TryMarkModelNotice(Guid chatId, Guid botId) => _modelNotices.TryAdd((chatId, botId), 0);
    public void ClearModelNotices(Guid botId)
    {
        foreach (var key in _modelNotices.Keys.Where(k => k.BotId == botId))
            _modelNotices.TryRemove(key, out _);
    }

    public DateTime? HistoryStart(Guid chatId, Guid botId) =>
        _historyStarts.TryGetValue((chatId, botId), out var start) ? start : null;
    public void SetHistoryStart(Guid chatId, Guid botId, DateTime start) => _historyStarts[(chatId, botId)] = start;

    private bool Contains(Guid chatId, Guid? botId) =>
        botId is { } id ? _muted.ContainsKey((chatId, id)) : _stopped.ContainsKey(chatId);

    private void Remember(Guid chatId, Guid? botId)
    {
        if (botId is { } id) _muted[(chatId, id)] = 0;
        else _stopped[chatId] = 0;
    }

    private void Silence(Guid chatId, Guid? botId)
    {
        EnsureLoaded();
        if (Contains(chatId, botId)) return;
        lock (_gate)
        {
            if (Contains(chatId, botId)) return;
            // The row goes in first, so a failed write leaves memory unchanged.
            Persist(db => db.BotSilences.Add(new BotSilence { ChatId = chatId, BotId = botId }));
            Remember(chatId, botId);
        }
    }

    private void Unsilence(Guid chatId, Guid? botId)
    {
        EnsureLoaded();
        // Resume runs on every human message, so the usual case must not touch the database.
        if (!Contains(chatId, botId)) return;
        lock (_gate)
        {
            if (!Contains(chatId, botId)) return;
            Persist(db => db.BotSilences.RemoveRange(db.BotSilences.Where(s => s.ChatId == chatId && s.BotId == botId)));
            if (botId is { } id) _muted.TryRemove((chatId, id), out _);
            else _stopped.TryRemove(chatId, out _);
        }
    }

    private void Persist(Action<NeuralDamageDbContext> change)
    {
        if (_scopes is null) return;
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
        change(db);
        db.SaveChanges();
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            using var scope = _scopes!.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuralDamageDbContext>();
            foreach (var silence in db.BotSilences.Select(s => new { s.ChatId, s.BotId }).ToList())
                Remember(silence.ChatId, silence.BotId);
            _loaded = true;
        }
    }
}
