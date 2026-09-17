using NeuralDamage.Infrastructure.Services;

namespace NeuralDamage.Tests.Infrastructure;

public class ConnectionTrackerTests
{
    private readonly ConnectionTracker _tracker = new();

    [Test]
    public async Task TrackConnection_AddsConnection()
    {
        var userId = Guid.NewGuid();

        _tracker.TrackConnection("conn-1", userId);

        await Assert.That(_tracker.IsUserOnline(userId)).IsTrue();
        await Assert.That(_tracker.GetConnections(userId)).HasSingleItem();
        await Assert.That(_tracker.GetConnections(userId)[0]).IsEqualTo("conn-1");
    }

    [Test]
    public async Task TrackConnection_MultipleConnections_SameUser()
    {
        var userId = Guid.NewGuid();

        _tracker.TrackConnection("conn-1", userId);
        _tracker.TrackConnection("conn-2", userId);

        await Assert.That(_tracker.GetConnections(userId).Count).IsEqualTo(2);
    }

    [Test]
    public async Task RemoveConnection_RemovesCorrectly()
    {
        var userId = Guid.NewGuid();
        _tracker.TrackConnection("conn-1", userId);
        _tracker.TrackConnection("conn-2", userId);

        _tracker.RemoveConnection("conn-1");

        await Assert.That(_tracker.IsUserOnline(userId)).IsTrue();
        await Assert.That(_tracker.GetConnections(userId)).HasSingleItem();
        await Assert.That(_tracker.GetConnections(userId)[0]).IsEqualTo("conn-2");
    }

    [Test]
    public async Task RemoveConnection_LastConnection_UserGoesOffline()
    {
        var userId = Guid.NewGuid();
        _tracker.TrackConnection("conn-1", userId);

        _tracker.RemoveConnection("conn-1");

        await Assert.That(_tracker.IsUserOnline(userId)).IsFalse();
        await Assert.That(_tracker.GetConnections(userId)).IsEmpty();
    }

    [Test]
    public void RemoveConnection_NonExistent_DoesNotThrow()
    {
        _tracker.RemoveConnection("nonexistent");
    }

    [Test]
    public async Task GetConnections_UnknownUser_ReturnsEmpty()
    {
        await Assert.That(_tracker.GetConnections(Guid.NewGuid())).IsEmpty();
    }

    [Test]
    public async Task IsUserOnline_UnknownUser_ReturnsFalse()
    {
        await Assert.That(_tracker.IsUserOnline(Guid.NewGuid())).IsFalse();
    }

    [Test]
    public async Task MultipleUsers_TrackedIndependently()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        _tracker.TrackConnection("conn-1", user1);
        _tracker.TrackConnection("conn-2", user2);

        await Assert.That(_tracker.GetConnections(user1)).HasSingleItem();
        await Assert.That(_tracker.GetConnections(user2)).HasSingleItem();

        _tracker.RemoveConnection("conn-1");

        await Assert.That(_tracker.IsUserOnline(user1)).IsFalse();
        await Assert.That(_tracker.IsUserOnline(user2)).IsTrue();
    }
}
