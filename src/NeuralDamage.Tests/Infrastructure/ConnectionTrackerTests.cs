using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Tests.Infrastructure;

public class ConnectionTrackerTests
{
    private readonly ConnectionTracker _tracker = new();

    [Fact]
    public void TrackConnection_AddsConnection()
    {
        var userId = Guid.NewGuid();

        _tracker.TrackConnection("conn-1", userId);

        Assert.True(_tracker.IsUserOnline(userId));
        Assert.Single(_tracker.GetConnections(userId));
        Assert.Equal("conn-1", _tracker.GetConnections(userId)[0]);
    }

    [Fact]
    public void TrackConnection_MultipleConnections_SameUser()
    {
        var userId = Guid.NewGuid();

        _tracker.TrackConnection("conn-1", userId);
        _tracker.TrackConnection("conn-2", userId);

        Assert.Equal(2, _tracker.GetConnections(userId).Count);
    }

    [Fact]
    public void RemoveConnection_RemovesCorrectly()
    {
        var userId = Guid.NewGuid();
        _tracker.TrackConnection("conn-1", userId);
        _tracker.TrackConnection("conn-2", userId);

        _tracker.RemoveConnection("conn-1");

        Assert.True(_tracker.IsUserOnline(userId));
        Assert.Single(_tracker.GetConnections(userId));
        Assert.Equal("conn-2", _tracker.GetConnections(userId)[0]);
    }

    [Fact]
    public void RemoveConnection_LastConnection_UserGoesOffline()
    {
        var userId = Guid.NewGuid();
        _tracker.TrackConnection("conn-1", userId);

        _tracker.RemoveConnection("conn-1");

        Assert.False(_tracker.IsUserOnline(userId));
        Assert.Empty(_tracker.GetConnections(userId));
    }

    [Fact]
    public void RemoveConnection_NonExistent_DoesNotThrow()
    {
        _tracker.RemoveConnection("nonexistent");
    }

    [Fact]
    public void GetConnections_UnknownUser_ReturnsEmpty()
    {
        Assert.Empty(_tracker.GetConnections(Guid.NewGuid()));
    }

    [Fact]
    public void IsUserOnline_UnknownUser_ReturnsFalse()
    {
        Assert.False(_tracker.IsUserOnline(Guid.NewGuid()));
    }

    [Fact]
    public void MultipleUsers_TrackedIndependently()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        _tracker.TrackConnection("conn-1", user1);
        _tracker.TrackConnection("conn-2", user2);

        Assert.Single(_tracker.GetConnections(user1));
        Assert.Single(_tracker.GetConnections(user2));

        _tracker.RemoveConnection("conn-1");

        Assert.False(_tracker.IsUserOnline(user1));
        Assert.True(_tracker.IsUserOnline(user2));
    }
}
