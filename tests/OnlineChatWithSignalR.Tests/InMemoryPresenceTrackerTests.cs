using OnlineChatWithSignalR.Chat;

namespace OnlineChatWithSignalR.Tests;

public sealed class InMemoryPresenceTrackerTests
{
    private readonly InMemoryPresenceTracker _tracker = new();

    [Fact]
    public void FirstConnection_ComesOnline_NextOnesDoNot()
    {
        Assert.True(_tracker.Connected("alice", "tab-1"));
        Assert.False(_tracker.Connected("alice", "tab-2"));
        Assert.True(_tracker.IsOnline("alice"));
    }

    [Fact]
    public void LastDisconnect_GoesOffline()
    {
        _tracker.Connected("alice", "tab-1");
        _tracker.Connected("alice", "tab-2");

        Assert.False(_tracker.Disconnected("alice", "tab-1"));
        Assert.True(_tracker.IsOnline("alice"));

        Assert.True(_tracker.Disconnected("alice", "tab-2"));
        Assert.False(_tracker.IsOnline("alice"));
    }

    [Fact]
    public void UnknownDisconnect_IsIgnored()
    {
        _tracker.Connected("alice", "tab-1");

        Assert.False(_tracker.Disconnected("alice", "unknown"));
        Assert.False(_tracker.Disconnected("bob", "tab-1"));
        Assert.True(_tracker.IsOnline("alice"));
    }

    [Fact]
    public void GetConnections_ReturnsACopy()
    {
        _tracker.Connected("alice", "tab-1");
        _tracker.Connected("alice", "tab-2");

        var connections = _tracker.GetConnections("alice");
        _tracker.Disconnected("alice", "tab-1");

        Assert.Equal(2, connections.Count);
        Assert.Empty(_tracker.GetConnections("bob"));
    }

    [Fact]
    public async Task ConcurrentConnects_AreCountedCorrectly()
    {
        var cameOnline = 0;
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            if (_tracker.Connected("alice", $"tab-{i}"))
            {
                Interlocked.Increment(ref cameOnline);
            }
        })));

        Assert.Equal(1, cameOnline);
        Assert.Equal(100, _tracker.GetConnections("alice").Count);
    }
}
