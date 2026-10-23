namespace OnlineChatWithSignalR.Chat;

/// <summary>
/// Presence for a single server: the state lives in the memory of this process.
/// Used when no Redis connection is configured (local development with one instance).
/// With several API instances each server would only see its own connections, so
/// <see cref="RedisPresenceTracker"/> is used instead.
/// </summary>
public sealed class InMemoryPresenceTracker : IPresenceTracker
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

    Task<bool> IPresenceTracker.ConnectedAsync(string userId, string connectionId) =>
        Task.FromResult(Connected(userId, connectionId));

    Task<bool> IPresenceTracker.DisconnectedAsync(string userId, string connectionId) =>
        Task.FromResult(Disconnected(userId, connectionId));

    Task<bool> IPresenceTracker.IsOnlineAsync(string userId) =>
        Task.FromResult(IsOnline(userId));

    Task<IReadOnlyList<string>> IPresenceTracker.GetConnectionsAsync(string userId) =>
        Task.FromResult(GetConnections(userId));
}
