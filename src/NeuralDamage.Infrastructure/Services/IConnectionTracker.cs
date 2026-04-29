namespace NeuralDamage.Infrastructure.Services;

public interface IConnectionTracker
{
    void TrackConnection(string connectionId, Guid userId);
    void RemoveConnection(string connectionId);
    IReadOnlyList<string> GetConnections(Guid userId);
    bool IsUserOnline(Guid userId);
}
