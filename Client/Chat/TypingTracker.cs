namespace Client.Chat;

/// <summary>
/// Who is typing in which room, as seen by this client. Pure C# with an injectable clock,
/// so the timing rules are unit tested.
/// <list type="bullet">
/// <item>Receiving: a "typing" signal shows the user for <see cref="DisplayDuration"/>;
/// every new signal extends it. No "stopped" message is needed, so a closed tab or lost
/// connection can never leave the indicator stuck.</item>
/// <item>Sending: <see cref="ShouldSendTyping"/> allows one signal per <see cref="SendInterval"/>,
/// so typing a sentence sends a few messages instead of one per keystroke.</item>
/// </list>
/// </summary>
public sealed class TypingTracker(TimeProvider timeProvider)
{
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<(Guid RoomId, string UserName), DateTimeOffset> _visibleUntil = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastSent = [];

    public void OnTyping(Guid roomId, string userName) =>
        _visibleUntil[(roomId, userName)] = timeProvider.GetUtcNow() + DisplayDuration;

    /// <summary>The user sent a message, so they are no longer typing it.</summary>
    public void OnMessage(Guid roomId, string userName) =>
        _visibleUntil.Remove((roomId, userName));

    public IReadOnlyList<string> GetTypingUsers(Guid roomId)
    {
        var now = timeProvider.GetUtcNow();
        return _visibleUntil
            .Where(entry => entry.Key.RoomId == roomId && entry.Value > now)
            .Select(entry => entry.Key.UserName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Removes expired entries. Returns true if anything changed (the UI should re-render).</summary>
    public bool RemoveExpired()
    {
        var now = timeProvider.GetUtcNow();
        var expired = _visibleUntil.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList();
        foreach (var key in expired)
        {
            _visibleUntil.Remove(key);
        }

        return expired.Count > 0;
    }

    /// <summary>True if this client should send a typing signal for the room now.</summary>
    public bool ShouldSendTyping(Guid roomId)
    {
        var now = timeProvider.GetUtcNow();
        if (_lastSent.TryGetValue(roomId, out var last) && now - last < SendInterval)
        {
            return false;
        }

        _lastSent[roomId] = now;
        return true;
    }

    /// <summary>Call after sending a message, so the next keystroke signals typing again right away.</summary>
    public void ResetSendThrottle(Guid roomId) => _lastSent.Remove(roomId);

    public static string? Describe(IReadOnlyList<string> userNames) => userNames.Count switch
    {
        0 => null,
        1 => $"{userNames[0]} is typing...",
        2 => $"{userNames[0]} and {userNames[1]} are typing...",
        _ => "Several people are typing..."
    };
}
