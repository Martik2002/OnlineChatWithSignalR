namespace OnlineChatWithSignalR.Chat;

/// <summary>
/// Tracks which users are online and through which connections.
/// <para>
/// A user is online while they have at least one open connection. Counting connections
/// (instead of a simple online flag) matters because one user often has several tabs or
/// devices open: closing one tab must not mark them offline.
/// </para>
/// </summary>
public interface IPresenceTracker
{
    /// <returns>True if this is the user's first connection, i.e. the user just came online.</returns>
    Task<bool> ConnectedAsync(string userId, string connectionId);

    /// <returns>True if this was the user's last connection, i.e. the user just went offline.</returns>
    Task<bool> DisconnectedAsync(string userId, string connectionId);

    Task<bool> IsOnlineAsync(string userId);

    /// <summary>All open connections of the user, on every server.</summary>
    Task<IReadOnlyList<string>> GetConnectionsAsync(string userId);
}
