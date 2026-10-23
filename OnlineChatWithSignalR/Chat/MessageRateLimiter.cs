using System.Threading.RateLimiting;

namespace OnlineChatWithSignalR.Chat;

public sealed class MessageRateLimitOptions
{
    public const string Section = "RateLimiting:Messages";

    public int PermitLimit { get; set; } = 20;
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Limits how many messages one user can send per time window (sliding window), against spam
/// and runaway clients. HTTP rate limiting middleware doesn't help here: a SignalR connection
/// is one long-lived request, and every message is an invocation inside it.
/// <para>
/// The counters live in this server's memory, so with N instances a user could send up to
/// N times the limit. That is an accepted trade-off: the goal is to stop spam, not exact
/// accounting, and it avoids a Redis round trip for every message.
/// </para>
/// </summary>
public sealed class MessageRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public MessageRateLimiter(Microsoft.Extensions.Options.IOptions<MessageRateLimitOptions> options)
    {
        var settings = options.Value;
        _limiter = PartitionedRateLimiter.Create<string, string>(userId =>
            RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = settings.PermitLimit,
                Window = settings.Window,
                SegmentsPerWindow = 5,
                QueueLimit = 0
            }));
    }

    /// <returns>True if the user may send a message now.</returns>
    public bool TryAcquire(string userId)
    {
        using var lease = _limiter.AttemptAcquire(userId);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
