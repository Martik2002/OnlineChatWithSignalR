using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OnlineChatWithSignalR.Chat;
using OnlineChatWithSignalR.Data;
using OnlineChatWithSignalR.Hubs;
using StackExchange.Redis;

namespace OnlineChatWithSignalR.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChatServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Chat");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'Chat' is missing. Set ConnectionStrings:Chat in configuration " +
                "or the ConnectionStrings__Chat environment variable.");
        }

        services.AddDbContext<ChatDbContext>(options => options.UseNpgsql(connectionString));

        services.AddIdentityApiEndpoints<ChatUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddEntityFrameworkStores<ChatDbContext>();

        services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
        {
            options.Events = new BearerTokenEvents
            {
                // Browsers can't send an Authorization header when opening a WebSocket,
                // so the SignalR client sends the token as ?access_token=... instead.
                // We only accept it from the query string for hub requests: tokens in URLs
                // can end up in logs, so regular API calls must use the header.
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ChatService>();

        services.Configure<MessageRateLimitOptions>(configuration.GetSection(MessageRateLimitOptions.Section));
        services.AddSingleton<MessageRateLimiter>();

        var signalR = services.AddSignalR(options => options.AddFilter<ChatExceptionHubFilter>());

        services.AddProblemDetails();
        services.AddOpenApi();

        var healthChecks = services.AddHealthChecks()
            .AddDbContextCheck<ChatDbContext>();

        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            // Single instance: everything in memory
            services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();
        }
        else
        {
            services.AddRedisScaleOut(signalR, healthChecks, redisConnectionString);
        }

        return services;
    }

    /// <summary>
    /// Everything several API instances need to work as one chat:
    /// <list type="bullet">
    /// <item>SignalR backplane: a message sent on one server reaches connections on every server.</item>
    /// <item>Presence in Redis, so every server sees every user's connections.</item>
    /// <item>Shared Data Protection keys: Identity bearer tokens are encrypted with these keys,
    /// so without sharing them a token issued by one server is rejected by the others.</item>
    /// </list>
    /// </summary>
    private static void AddRedisScaleOut(
        this IServiceCollection services,
        ISignalRServerBuilder signalR,
        IHealthChecksBuilder healthChecks,
        string redisConnectionString)
    {
        // Connect at startup: if Redis is unreachable the instance should fail fast
        // instead of running in a half-working state
        var redis = ConnectionMultiplexer.Connect(redisConnectionString);
        services.AddSingleton<IConnectionMultiplexer>(redis);

        signalR.AddStackExchangeRedis(redisConnectionString, options =>
        {
            // Keeps this app's pub/sub channels separate if the Redis server is shared
            options.Configuration.ChannelPrefix = RedisChannel.Literal("chat");
        });

        services.AddDataProtection()
            .SetApplicationName("OnlineChatWithSignalR")
            .PersistKeysToStackExchangeRedis(redis, "chat:data-protection-keys");

        services.AddSingleton<RedisPresenceTracker>();
        services.AddSingleton<IPresenceTracker>(provider => provider.GetRequiredService<RedisPresenceTracker>());
        services.AddHostedService<PresenceHeartbeat>();

        healthChecks.AddCheck<RedisHealthCheck>("redis");
    }
}
