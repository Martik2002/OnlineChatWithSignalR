using OnlineChatWithSignalR.Chat;

namespace Client.Chat;

public enum ChatItemStatus
{
    /// <summary>Saved on the server.</summary>
    Sent,

    /// <summary>Shown immediately, waiting for the server to confirm.</summary>
    Pending,

    /// <summary>Could not be sent; will be retried with the same id.</summary>
    Failed
}

public sealed record ChatItem(
    Guid Id,
    long? Sequence,
    string SenderName,
    string Content,
    DateTime? SentAt,
    ChatItemStatus Status,
    bool IsOwn);

/// <summary>
/// The messages of one room as shown on screen. Pure C# (no UI, no SignalR), so the tricky
/// merge rules are easy to unit test:
/// <list type="bullet">
/// <item>a message can arrive several times (own echo, catch-up after reconnect); it is shown once</item>
/// <item>confirmed messages are ordered by their server sequence</item>
/// <item>pending messages stay at the end until the server confirms them</item>
/// </list>
/// </summary>
public sealed class RoomTimeline(string currentUserName)
{
    private readonly List<ChatItem> _confirmed = [];
    private readonly List<ChatItem> _pending = [];

    public bool IsLoaded { get; private set; }
    public bool HasOlder { get; private set; }

    public IReadOnlyList<ChatItem> Items => [.. _confirmed, .. _pending];

    public IReadOnlyList<ChatItem> Unconfirmed => _pending.ToList();

    public long? OldestSequence => _confirmed.Count > 0 ? _confirmed[0].Sequence : null;
    public long? NewestSequence => _confirmed.Count > 0 ? _confirmed[^1].Sequence : null;

    /// <summary>Replaces the confirmed messages with the latest page (first load).</summary>
    public void LoadLatest(MessagePage page)
    {
        _confirmed.Clear();
        foreach (var message in page.Messages)
        {
            Upsert(message);
        }

        HasOlder = page.HasMore;
        IsLoaded = true;
    }

    /// <summary>Adds an older page loaded with "load older messages".</summary>
    public void AddOlder(MessagePage page)
    {
        foreach (var message in page.Messages)
        {
            Upsert(message);
        }

        HasOlder = page.HasMore;
    }

    /// <summary>
    /// Merges the latest page after a reconnect. If the page doesn't connect to what we already
    /// have (more messages were missed than fit in one page), the timeline is reloaded instead,
    /// so the user never sees a silent gap in the conversation.
    /// </summary>
    public void CatchUp(MessagePage latest)
    {
        // Sequence numbers are unique across all rooms, so they are not consecutive within a room.
        // A gap exists only if the page doesn't overlap what we have AND older messages exist.
        var newest = NewestSequence;
        var pageOldest = latest.Messages.Count > 0 ? latest.Messages[0].Sequence : (long?)null;
        var hasGap = newest is not null && pageOldest is not null && pageOldest > newest && latest.HasMore;

        if (hasGap)
        {
            LoadLatest(latest);
            return;
        }

        foreach (var message in latest.Messages)
        {
            Upsert(message);
        }
    }

    public ChatItem AddPending(Guid id, string content)
    {
        var item = new ChatItem(id, null, currentUserName, content, null, ChatItemStatus.Pending, IsOwn: true);
        _pending.Add(item);
        return item;
    }

    public void MarkFailed(Guid id)
    {
        var index = _pending.FindIndex(p => p.Id == id);
        if (index >= 0)
        {
            _pending[index] = _pending[index] with { Status = ChatItemStatus.Failed };
        }
    }

    public void MarkPending(Guid id)
    {
        var index = _pending.FindIndex(p => p.Id == id);
        if (index >= 0)
        {
            _pending[index] = _pending[index] with { Status = ChatItemStatus.Pending };
        }
    }

    /// <summary>
    /// Adds or updates a message from the server (live message, send confirmation, or history).
    /// Returns false if it was already shown.
    /// </summary>
    public bool Upsert(MessageDto message)
    {
        _pending.RemoveAll(p => p.Id == message.Id);

        var item = new ChatItem(
            message.Id,
            message.Sequence,
            message.SenderName,
            message.Content,
            message.SentAt,
            ChatItemStatus.Sent,
            IsOwn: message.SenderName == currentUserName);

        if (_confirmed.Any(c => c.Id == message.Id))
        {
            return false;
        }

        // Insert in sequence order. New messages usually go to the end, so search from there.
        var index = _confirmed.Count;
        while (index > 0 && _confirmed[index - 1].Sequence > message.Sequence)
        {
            index--;
        }

        _confirmed.Insert(index, item);
        return true;
    }
}
