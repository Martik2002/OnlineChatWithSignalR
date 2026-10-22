using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Testcontainers.PostgreSql;

namespace OnlineChatWithSignalR.Tests.Infrastructure;

/// <summary>
/// Runs the real API in memory against a real PostgreSQL in Docker.
/// </summary>
public sealed class ChatApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Str0ng!Password";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        // Read by Program while the host is built, so they must be set before the first client is created
        Environment.SetEnvironmentVariable("ConnectionStrings__Chat", _database.GetConnectionString());
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    /// <summary>Registers a new user and returns its email and access token.</summary>
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
    /// Creates a SignalR connection to the in-memory server. The test server can't do real
    /// WebSockets through its HTTP handler, so long polling is used as the transport.
    /// </summary>
    public HubConnection CreateHubConnection(string? accessToken)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();
    }

    private sealed record LoginResponse(string AccessToken, string RefreshToken, int ExpiresIn);
}

[CollectionDefinition(Name)]
public sealed class ChatApiCollection : ICollectionFixture<ChatApiFactory>
{
    public const string Name = "ChatApi";
}
