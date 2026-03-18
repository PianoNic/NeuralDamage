using System.Collections.Concurrent;
using NeuralDamage.Application.Interfaces;

namespace NeuralDamage.Infrastructure.Services;

public class ConnectionTracker : IConnectionTracker
{
    private readonly ConcurrentDictionary<string, Guid> _connectionToUser = new();
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _userToConnections = new();
    private readonly object _lock = new();

    public void TrackConnection(string connectionId, Guid userId)
    {
        _connectionToUser[connectionId] = userId;
        lock (_lock)
        {
            if (!_userToConnections.TryGetValue(userId, out var connections))
            {
                connections = [];
                _userToConnections[userId] = connections;
            }
            connections.Add(connectionId);
        }
    }

    public void RemoveConnection(string connectionId)
    {
        if (!_connectionToUser.TryRemove(connectionId, out var userId))
            return;

        lock (_lock)
        {
            if (_userToConnections.TryGetValue(userId, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                    _userToConnections.TryRemove(userId, out _);
            }
        }
    }

    public IReadOnlyList<string> GetConnections(Guid userId)
    {
        lock (_lock)
        {
            return _userToConnections.TryGetValue(userId, out var connections)
                ? connections.ToList()
                : [];
        }
    }

    public bool IsUserOnline(Guid userId) => _userToConnections.ContainsKey(userId);
}
