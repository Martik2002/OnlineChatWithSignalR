using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace OnlineChatWithSignalR.Tests.Infrastructure;

/// <summary>
/// Runs the real API in memory against a real PostgreSQL and Redis in Docker, configured
/// like production (Redis backplane, presence and Data Protection keys in Redis).
/// <see cref="SecondInstance"/> is another API instance sharing the same PostgreSQL and Redis,
/// like a second server behind a load balancer.
/// </summary>
public sealed class ChatApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Str0ng!Password";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private readonly Lazy<ApiInstance> _secondInstance = new(() => new ApiInstance());

    /// <summary>A second API server. Created on first use.</summary>
    public WebApplicationFactory<Program> SecondInstance => _secondInstance.Value;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_database.StartAsync(), _redis.StartAsync());

        // Read by Program while the host is built, so they must be set before the first client
        // is created. Both API instances read the same values.
        Environment.SetEnvironmentVariable("ConnectionStrings__Chat", _database.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    public new async Task DisposeAsync()
    {
        if (_secondInstance.IsValueCreated)
        {
            await _secondInstance.Value.DisposeAsync();
        }

        await base.DisposeAsync();
        await Task.WhenAll(_database.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    /// <summary>Registers a new user (on the first instance) and returns its email and access token.</summary>
    public async Task<(string Email, string AccessToken)> CreateUserAsync()
    {
        var client = CreateClient();
        var email = $"user_{Guid.NewGuid():N}@example.com";

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
        return (email, tokens!.AccessToken);
    }

    /// <summary>
    /// Creates a SignalR connection to an in-memory server (this one by default). The test server
    /// can't do real WebSockets through its HTTP handler, so long polling is used as the transport.
    /// </summary>
    public HubConnection CreateHubConnection(string? accessToken, WebApplicationFactory<Program>? instance = null)
    {
        var server = (instance ?? this).Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();
    }

    /// <summary>Creates a new user and returns a started hub connection for it.</summary>
    public async Task<ChatTestClient> ConnectNewUserAsync(WebApplicationFactory<Program>? instance = null)
    {
        var (email, token) = await CreateUserAsync();
        var connection = CreateHubConnection(token, instance);
        var client = new ChatTestClient(email, token, connection);
        await connection.StartAsync();
        return client;
    }

    /// <summary>Opens another connection for an existing user, like a second browser tab.</summary>
    public async Task<ChatTestClient> OpenTabAsync(ChatTestClient user, WebApplicationFactory<Program>? instance = null)
    {
        var tab = new ChatTestClient(user.Email, user.AccessToken, CreateHubConnection(user.AccessToken, instance));
        await tab.Connection.StartAsync();
        return tab;
    }

    private sealed record LoginResponse(string AccessToken, string RefreshToken, int ExpiresIn);

    private sealed class ApiInstance : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ChatApiCollection : ICollectionFixture<ChatApiFactory>
{
    public const string Name = "ChatApi";
}
