using System.Net;
using Microsoft.AspNetCore.SignalR.Client;
using OnlineChatWithSignalR.Tests.Infrastructure;

namespace OnlineChatWithSignalR.Tests;

[Collection(ChatApiCollection.Name)]
public sealed class ChatHubAuthenticationTests(ChatApiFactory factory)
{
    [Fact]
    public async Task Connect_WithValidToken_Succeeds()
    {
        var (email, token) = await factory.CreateUserAsync();
        await using var connection = factory.CreateHubConnection(token);

        await connection.StartAsync();
        var name = await connection.InvokeAsync<string>("WhoAmI");

        Assert.Equal(HubConnectionState.Connected, connection.State);
        Assert.Equal(email, name);
    }

    [Fact]
    public async Task Connect_ReceivesWelcomeMessage()
    {
        var (email, token) = await factory.CreateUserAsync();
        await using var connection = factory.CreateHubConnection(token);

        var welcome = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<string>("ReceiveSystemMessage", message => welcome.TrySetResult(message));

        await connection.StartAsync();
        var message = await welcome.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(email, message);
    }

    [Fact]
    public async Task Connect_WithoutToken_IsRejected()
    {
        await using var connection = factory.CreateHubConnection(accessToken: null);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task Connect_WithInvalidToken_IsRejected()
    {
        await using var connection = factory.CreateHubConnection("not-a-real-token");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task Hub_AcceptsTokenFromQueryString()
    {
        // This is how browsers authenticate WebSocket connections
        var (_, token) = await factory.CreateUserAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/hubs/chat/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token)}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_DoesNotAcceptTokenFromQueryString()
    {
        // Tokens in URLs can leak into logs, so only hub requests may use them
        var (_, token) = await factory.CreateUserAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/auth/manage/info?access_token={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
