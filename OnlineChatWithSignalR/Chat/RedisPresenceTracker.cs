using System.Collections.Concurrent;
using StackExchange.Redis;

namespace OnlineChatWithSignalR.Chat;

/// <summary>
/// Presence shared by all API instances, stored in Redis.
/// <para>
/// Each user has a sorted set <c>chat:presence:{userId}</c>: the members are connection ids,
/// the scores are the time each connection was last seen. Every server refreshes the scores of
/// its own connections regularly (<see cref="RefreshLocalConnectionsAsync"/>). Entries that were
/// not refreshed for <see cref="ConnectionTimeout"/> are ignored and cleaned up.
/// </para>
/// <para>
/// Why the heartbeat: if a server crashes, its connections never get a "disconnected" event.
/// Without expiry, those users would stay "online" forever.
/// </para>
/// <para>
/// Adding/removing a connection and counting the rest happens in one Lua script, which Redis
/// runs atomically. Otherwise two servers could both see "1 connection" at the same moment
/// and both (or neither) would announce the user as online.
/// </para>
/// </summary>
public sealed class RedisPresenceTracker(IConnectionMultiplexer redis, TimeProvider timeProvider) : IPresenceTracker
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(90);

    // KEYS[1] = user key; ARGV[1] = now, ARGV[2] = stale cutoff, ARGV[3] = connection id, ARGV[4] = key TTL (ms)
    private const string ConnectScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[2])
        redis.call('ZADD', KEYS[1], ARGV[1], ARGV[3])
        redis.call('PEXPIRE', KEYS[1], ARGV[4])
        return redis.call('ZCARD', KEYS[1])
        """;

    // KEYS[1] = user key; ARGV[1] = stale cutoff, ARGV[2] = connection id
    private const string DisconnectScript = """
        redis.call('ZREM', KEYS[1], ARGV[2])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
        return redis.call('ZCARD', KEYS[1])
        """;

    // Connections handled by this server: connectionId -> userId. Used for the heartbeat.
    private readonly ConcurrentDictionary<string, string> _localConnections = new();

    private IDatabase Db => redis.GetDatabase();

    public async Task<bool> ConnectedAsync(string userId, string connectionId)
    {
        _localConnections[connectionId] = userId;

        var count = (long)await Db.ScriptEvaluateAsync(
            ConnectScript,
            [Key(userId)],
            [Now(), StaleCutoff(), connectionId, (long)(ConnectionTimeout * 2).TotalMilliseconds]);

        return count == 1;
    }

    public async Task<bool> DisconnectedAsync(string userId, string connectionId)
    {
        _localConnections.TryRemove(connectionId, out _);

        var count = (long)await Db.ScriptEvaluateAsync(
            DisconnectScript,
            [Key(userId)],
            [StaleCutoff(), connectionId]);

        return count == 0;
    }

    public async Task<bool> IsOnlineAsync(string userId) =>
        await Db.SortedSetLengthAsync(Key(userId), min: StaleCutoff()) > 0;

    public async Task<IReadOnlyList<string>> GetConnectionsAsync(string userId)
    {
        var connections = await Db.SortedSetRangeByScoreAsync(Key(userId), start: StaleCutoff());
        return connections.Select(c => c.ToString()).ToList();
    }

    /// <summary>Marks all connections of this server as still alive.</summary>
    public async Task RefreshLocalConnectionsAsync()
    {
        var now = Now();
        var keyTtl = ConnectionTimeout * 2;
        var batch = Db.CreateBatch();
        var tasks = new List<Task>();

        foreach (var (connectionId, userId) in _localConnections)
        {
            // XX: only update connections that still exist (don't revive a removed one)
            tasks.Add(batch.SortedSetAddAsync(Key(userId), connectionId, now, SortedSetWhen.Exists));
            tasks.Add(batch.KeyExpireAsync(Key(userId), keyTtl));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    private static RedisKey Key(string userId) => $"chat:presence:{userId}";

    private double Now() => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    private double StaleCutoff() => (timeProvider.GetUtcNow() - ConnectionTimeout).ToUnixTimeMilliseconds();
}

/// <summary>Runs the presence heartbeat for this server's connections.</summary>
public sealed class PresenceHeartbeat(RedisPresenceTracker tracker, ILogger<PresenceHeartbeat> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RedisPresenceTracker.HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await tracker.RefreshLocalConnectionsAsync();
            }
            catch (RedisException ex)
            {
                // Keep running: the next heartbeat may succeed once Redis is reachable again
                logger.LogWarning(ex, "Presence heartbeat failed");
            }
        }
    }
}
