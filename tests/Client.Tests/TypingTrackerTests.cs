using Client.Chat;

namespace Client.Tests;

public sealed class TypingTrackerTests
{
    private static readonly Guid Room = Guid.NewGuid();
    private static readonly Guid OtherRoom = Guid.NewGuid();

    private readonly ManualClock _clock = new();
    private readonly TypingTracker _tracker;

    public TypingTrackerTests()
    {
        _tracker = new TypingTracker(_clock);
    }

    [Fact]
    public void TypingUser_IsShown_UntilTimeout()
    {
        _tracker.OnTyping(Room, "bob");
        Assert.Equal(new[] { "bob" }, _tracker.GetTypingUsers(Room));

        _clock.Advance(TypingTracker.DisplayDuration - TimeSpan.FromMilliseconds(1));
        Assert.Single(_tracker.GetTypingUsers(Room));

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Empty(_tracker.GetTypingUsers(Room));
    }

    [Fact]
    public void NewSignal_ExtendsTheIndicator()
    {
        _tracker.OnTyping(Room, "bob");
        _clock.Advance(TimeSpan.FromSeconds(3));
        _tracker.OnTyping(Room, "bob");
        _clock.Advance(TimeSpan.FromSeconds(3));

        Assert.Single(_tracker.GetTypingUsers(Room));
    }

    [Fact]
    public void Message_HidesTheIndicatorImmediately()
    {
        _tracker.OnTyping(Room, "bob");

        _tracker.OnMessage(Room, "bob");

        Assert.Empty(_tracker.GetTypingUsers(Room));
    }

    [Fact]
    public void Rooms_AreSeparate()
    {
        _tracker.OnTyping(Room, "bob");

        Assert.Empty(_tracker.GetTypingUsers(OtherRoom));
    }

    [Fact]
    public void RemoveExpired_ReportsWhenSomethingChanged()
    {
        _tracker.OnTyping(Room, "bob");
        Assert.False(_tracker.RemoveExpired());

        _clock.Advance(TypingTracker.DisplayDuration);
        Assert.True(_tracker.RemoveExpired());
        Assert.False(_tracker.RemoveExpired());
    }

    [Fact]
    public void ShouldSendTyping_IsThrottledPerRoom()
    {
        Assert.True(_tracker.ShouldSendTyping(Room));
        Assert.False(_tracker.ShouldSendTyping(Room));
        Assert.True(_tracker.ShouldSendTyping(OtherRoom));

        _clock.Advance(TypingTracker.SendInterval);
        Assert.True(_tracker.ShouldSendTyping(Room));
    }

    [Fact]
    public void ResetSendThrottle_AllowsSendingAgainImmediately()
    {
        _tracker.ShouldSendTyping(Room);

        _tracker.ResetSendThrottle(Room);

        Assert.True(_tracker.ShouldSendTyping(Room));
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "bob" }, "bob is typing...")]
    [InlineData(new[] { "alice", "bob" }, "alice and bob are typing...")]
    [InlineData(new[] { "alice", "bob", "carol" }, "Several people are typing...")]
    public void Describe_FormatsTheIndicatorText(string[] users, string? expected)
    {
        Assert.Equal(expected, TypingTracker.Describe(users));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
