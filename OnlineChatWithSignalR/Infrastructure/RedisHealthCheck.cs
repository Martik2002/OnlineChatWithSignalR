using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace OnlineChatWithSignalR.Infrastructure;

/// <summary>Reports unhealthy when Redis can't be reached, so a load balancer can take the instance out.</summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis ping {latency.TotalMilliseconds:F0} ms");
        }
        catch (RedisException ex)
        {
            return HealthCheckResult.Unhealthy("Redis is not reachable.", ex);
        }
    }
}
