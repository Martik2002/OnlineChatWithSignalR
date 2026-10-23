using Microsoft.AspNetCore.SignalR;
using OnlineChatWithSignalR.Tests.Infrastructure;

namespace OnlineChatWithSignalR.Tests;

/// <summary>
/// Two API instances sharing PostgreSQL and Redis, like two servers behind a load balancer.
/// Without the Redis backplane, presence and shared keys, every test here fails.
/// </summary>
[Collection(ChatApiCollection.Name)]
public sealed class ScaleOutTests(ChatApiFactory factory)
{
    [Fact]
    public async Task TokenFromOneServer_IsAcceptedByTheOther()
    {
        // Users log in on server 1; the token is encrypted with the Data Protection keys,
        // which are shared through Redis
        await using var bob = await factory.ConnectNewUserAsync(factory.SecondInstance);

        var name = await bob.Connection.InvokeAsync<string>("WhoAmI");

        Assert.Equal(bob.Email, name);
    }

    [Fact]
    public async Task Message_ReachesUserOnAnotherServer()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var bob = await factory.ConnectNewUserAsync(factory.SecondInstance);
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);

        var sent = await alice.SendAsync(room.Id, "hello from server 1");

        var received = await bob.WaitForMessageAsync(m => m.Id == sent.Id);
        Assert.Equal("hello from server 1", received.Content);
    }

    [Fact]
    public async Task JoinRoom_OnOneServer_SubscribesTabsOnTheOtherServer()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var bob = await factory.ConnectNewUserAsync();
        await using var bobOnSecondServer = await factory.OpenTabAsync(bob, factory.SecondInstance);
        var room = await alice.CreateRoomAsync();

        await bob.JoinRoomAsync(room.Id);
        var sent = await alice.SendAsync(room.Id, "both tabs, both servers");

        await bobOnSecondServer.WaitForMessageAsync(m => m.Id == sent.Id);
    }

    [Fact]
    public async Task Presence_IsSharedBetweenServers()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var bob = await factory.ConnectNewUserAsync(factory.SecondInstance);
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);

        // Server 1 knows that bob, connected to server 2, is online
        var members = await alice.GetRoomMembersAsync(room.Id);
        Assert.True(members.Single(m => m.UserName == bob.Email).IsOnline);

        await bob.DisposeAsync();
        await ChatTestClient.WaitUntilAsync(() =>
            alice.ReceivedPresence.Any(p => p.UserName == bob.Email && !p.IsOnline));

        members = await alice.GetRoomMembersAsync(room.Id);
        Assert.False(members.Single(m => m.UserName == bob.Email).IsOnline);
    }

    [Fact]
    public async Task UserWithTabsOnBothServers_StaysOnlineUntilTheLastOneCloses()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var bob = await factory.ConnectNewUserAsync();
        var bobOnSecondServer = await factory.OpenTabAsync(bob, factory.SecondInstance);
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);

        await bob.DisposeAsync();
        await ChatTestClient.QuietPeriod();
        Assert.DoesNotContain(alice.ReceivedPresence, p => p.UserName == bob.Email && !p.IsOnline);

        await bobOnSecondServer.DisposeAsync();
        await ChatTestClient.WaitUntilAsync(() =>
            alice.ReceivedPresence.Any(p => p.UserName == bob.Email && !p.IsOnline));
    }

    [Fact]
    public async Task SendingTooFast_IsRateLimited()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();

        // Default limit: 20 messages per 10 seconds per user
        for (var i = 0; i < 20; i++)
        {
            await alice.SendAsync(room.Id, $"message {i}");
        }

        var error = await Assert.ThrowsAsync<HubException>(() => alice.SendAsync(room.Id, "one too many"));
        Assert.Contains("too fast", error.Message);
    }
}
