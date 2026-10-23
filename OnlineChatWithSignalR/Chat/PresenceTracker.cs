namespace OnlineChatWithSignalR.Chat;

/// <summary>
/// Tracks which users are online and through which connections.
/// <para>
/// A user is online while they have at least one open connection. Counting connections
/// (instead of a simple online flag) matters because one user often has several tabs or
/// devices open: closing one tab must not mark them offline.
/// </para>
/// <para>
/// Limitation: the state lives in the memory of this server process. With several API
/// instances each server only sees its own connections; this is solved with Redis in #8.
/// </para>
/// </summary>
public sealed class PresenceTracker
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HashSet<string>> _connectionsByUser = [];

    /// <returns>True if this is the user's first connection, i.e. the user just came online.</returns>
    public bool Connected(string userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionsByUser.TryGetValue(userId, out var connections))
            {
                connections = [];
                _connectionsByUser[userId] = connections;
            }

            connections.Add(connectionId);
            return connections.Count == 1;
        }
    }

    /// <returns>True if this was the user's last connection, i.e. the user just went offline.</returns>
    public bool Disconnected(string userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionsByUser.TryGetValue(userId, out var connections) || !connections.Remove(connectionId))
            {
                return false;
            }

            if (connections.Count > 0)
            {
                return false;
            }

            _connectionsByUser.Remove(userId);
            return true;
        }
    }

    public bool IsOnline(string userId)
    {
        lock (_lock)
        {
            return _connectionsByUser.ContainsKey(userId);
        }
    }

    /// <summary>A copy of the user's open connection ids (safe to use outside the lock).</summary>
    public IReadOnlyList<string> GetConnections(string userId)
    {
        lock (_lock)
        {
            return _connectionsByUser.TryGetValue(userId, out var connections) ? connections.ToList() : [];
        }
    }
}
