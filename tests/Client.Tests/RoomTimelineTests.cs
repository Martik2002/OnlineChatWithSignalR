using Client.Chat;
using OnlineChatWithSignalR.Chat;

namespace Client.Tests;

public sealed class RoomTimelineTests
{
    private const string Me = "me@example.com";
    private const string Other = "other@example.com";
    private static readonly Guid RoomId = Guid.NewGuid();

    private static MessageDto Message(long sequence, string sender = Other, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), sequence, RoomId, "user-id", sender, $"message {sequence}", DateTime.UtcNow);

    private static MessagePage Page(bool hasMore, params MessageDto[] messages) => new(messages, hasMore);

    [Fact]
    public void LoadLatest_ShowsMessagesInOrder()
    {
        var timeline = new RoomTimeline(Me);

        timeline.LoadLatest(Page(hasMore: true, Message(3), Message(7)));

        Assert.True(timeline.IsLoaded);
        Assert.True(timeline.HasOlder);
        Assert.Equal(new long?[] { 3, 7 }, timeline.Items.Select(i => i.Sequence));
    }

    [Fact]
    public void Upsert_SameMessageTwice_IsShownOnce()
    {
        var timeline = new RoomTimeline(Me);
        var message = Message(5);

        Assert.True(timeline.Upsert(message));
        Assert.False(timeline.Upsert(message));

        Assert.Single(timeline.Items);
    }

    [Fact]
    public void Upsert_OutOfOrder_IsSortedBySequence()
    {
        var timeline = new RoomTimeline(Me);

        timeline.Upsert(Message(10));
        timeline.Upsert(Message(4));
        timeline.Upsert(Message(8));

        Assert.Equal(new long?[] { 4, 8, 10 }, timeline.Items.Select(i => i.Sequence));
    }

    [Fact]
    public void AddOlder_PrependsHistory()
    {
        var timeline = new RoomTimeline(Me);
        timeline.LoadLatest(Page(hasMore: true, Message(20), Message(21)));

        timeline.AddOlder(Page(hasMore: false, Message(11), Message(15)));

        Assert.Equal(new long?[] { 11, 15, 20, 21 }, timeline.Items.Select(i => i.Sequence));
        Assert.False(timeline.HasOlder);
        Assert.Equal(11, timeline.OldestSequence);
    }

    [Fact]
    public void PendingMessage_StaysAtTheEnd_UntilConfirmed()
    {
        var timeline = new RoomTimeline(Me);
        timeline.LoadLatest(Page(hasMore: false, Message(1)));
        var pending = timeline.AddPending(Guid.NewGuid(), "hello");

        // Someone else's message arrives while ours is still being sent
        timeline.Upsert(Message(2));

        Assert.Equal(pending.Id, timeline.Items[^1].Id);
        Assert.Equal(ChatItemStatus.Pending, timeline.Items[^1].Status);

        // The server confirms our message (also arrives as a live echo)
        timeline.Upsert(Message(3, Me, pending.Id));

        Assert.Equal(3, timeline.Items.Count);
        Assert.Empty(timeline.Unconfirmed);
        var confirmed = timeline.Items.Single(i => i.Id == pending.Id);
        Assert.Equal(ChatItemStatus.Sent, confirmed.Status);
        Assert.True(confirmed.IsOwn);
    }

    [Fact]
    public void FailedMessage_IsKeptForRetry()
    {
        var timeline = new RoomTimeline(Me);
        var pending = timeline.AddPending(Guid.NewGuid(), "hello");

        timeline.MarkFailed(pending.Id);

        var failed = Assert.Single(timeline.Unconfirmed);
        Assert.Equal(ChatItemStatus.Failed, failed.Status);
        Assert.Equal(pending.Id, failed.Id);
    }

    [Fact]
    public void CatchUp_OverlappingPage_MergesMissedMessages()
    {
        var timeline = new RoomTimeline(Me);
        timeline.LoadLatest(Page(hasMore: true, Message(10), Message(12)));

        // While offline, messages 14 and 17 were sent. The latest page overlaps with ours.
        timeline.CatchUp(Page(hasMore: true, Message(12), Message(14), Message(17)));

        Assert.Equal(new long?[] { 10, 12, 14, 17 }, timeline.Items.Select(i => i.Sequence));
        Assert.True(timeline.HasOlder);
    }

    [Fact]
    public void CatchUp_WithGap_ReloadsInsteadOfShowingAHole()
    {
        var timeline = new RoomTimeline(Me);
        timeline.LoadLatest(Page(hasMore: false, Message(10), Message(12)));

        // Too many messages were missed: the latest page starts after our newest message
        // and the server says there is more in between
        timeline.CatchUp(Page(hasMore: true, Message(90), Message(95)));

        Assert.Equal(new long?[] { 90, 95 }, timeline.Items.Select(i => i.Sequence));
        Assert.True(timeline.HasOlder);
    }

    [Fact]
    public void CatchUp_KeepsUnsentMessages()
    {
        var timeline = new RoomTimeline(Me);
        timeline.LoadLatest(Page(hasMore: false, Message(10)));
        var pending = timeline.AddPending(Guid.NewGuid(), "sent while offline");
        timeline.MarkFailed(pending.Id);

        timeline.CatchUp(Page(hasMore: true, Message(50), Message(60)));

        Assert.Contains(timeline.Unconfirmed, i => i.Id == pending.Id);
        Assert.Equal(pending.Id, timeline.Items[^1].Id);
    }
}
