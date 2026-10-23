using Microsoft.AspNetCore.SignalR;
using OnlineChatWithSignalR.Tests.Infrastructure;

namespace OnlineChatWithSignalR.Tests;

[Collection(ChatApiCollection.Name)]
public sealed class PresenceTests(ChatApiFactory factory)
{
    [Fact]
    public async Task RoomMembers_SeeUserGoOfflineAndBackOnline()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var bob = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);

        await bob.DisposeAsync();
        await ChatTestClient.WaitUntilAsync(() =>
            alice.ReceivedPresence.Any(p => p.UserName == bob.Email && !p.IsOnline));

        await using var bobAgain = await factory.OpenTabAsync(bob);
        await ChatTestClient.WaitUntilAsync(() =>
            alice.ReceivedPresence.Any(p => p.UserName == bob.Email && p.IsOnline));
    }

    [Fact]
    public async Task ClosingOneOfSeveralTabs_KeepsUserOnline()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var bob = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);
        var bobSecondTab = await factory.OpenTabAsync(bob);

        await bobSecondTab.DisposeAsync();
        await ChatTestClient.QuietPeriod();

        Assert.DoesNotContain(alice.ReceivedPresence, p => p.UserName == bob.Email && !p.IsOnline);
        var members = await alice.GetRoomMembersAsync(room.Id);
        Assert.True(members.Single(m => m.UserName == bob.Email).IsOnline);

        // Closing the last tab makes the user offline: first in the presence store...
        await bob.DisposeAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var bobOnline = true;
        while (bobOnline && DateTime.UtcNow < deadline)
        {
            members = await alice.GetRoomMembersAsync(room.Id);
            bobOnline = members.Single(m => m.UserName == bob.Email).IsOnline;
            if (bobOnline)
            {
                await Task.Delay(100);
            }
        }

        Assert.False(bobOnline, "Presence store still reports bob online after both tabs closed");

        // ...and then as an event to the other members
        await ChatTestClient.WaitUntilAsync(() =>
            alice.ReceivedPresence.Any(p => p.UserName == bob.Email && !p.IsOnline));
    }

    [Fact]
    public async Task Presence_IsOnlySentToUsersWhoShareARoom()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var stranger = await factory.ConnectNewUserAsync();
        var bob = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);

        await bob.DisposeAsync();
        await ChatTestClient.WaitUntilAsync(() =>
            alice.ReceivedPresence.Any(p => p.UserName == bob.Email && !p.IsOnline));

        Assert.DoesNotContain(stranger.ReceivedPresence, p => p.UserName == bob.Email);
    }

    [Fact]
    public async Task GetRoomMembers_ShowsWhoIsOnline()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var bob = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);

        var members = await alice.GetRoomMembersAsync(room.Id);
        Assert.Equal(2, members.Count);
        Assert.All(members, m => Assert.True(m.IsOnline));

        await bob.DisposeAsync();
        await ChatTestClient.WaitUntilAsync(() =>
            alice.ReceivedPresence.Any(p => p.UserName == bob.Email && !p.IsOnline));

        members = await alice.GetRoomMembersAsync(room.Id);
        Assert.False(members.Single(m => m.UserName == bob.Email).IsOnline);
        // Online members are listed first
        Assert.Equal(alice.Email, members[0].UserName);
    }

    [Fact]
    public async Task GetRoomMembers_ByNonMember_Fails()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var eve = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => eve.GetRoomMembersAsync(room.Id));

        Assert.Contains("not a member", error.Message);
    }
}
