using Microsoft.AspNetCore.SignalR;
using OnlineChatWithSignalR.Tests.Infrastructure;

namespace OnlineChatWithSignalR.Tests;

[Collection(ChatApiCollection.Name)]
public sealed class TypingAndTabsTests(ChatApiFactory factory)
{
    [Fact]
    public async Task StartTyping_NotifiesOtherRoomMembersOnly()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var bob = await factory.ConnectNewUserAsync();
        await using var eve = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);

        await alice.StartTypingAsync(room.Id);

        await ChatTestClient.WaitUntilAsync(() =>
            bob.ReceivedTyping.Any(t => t.RoomId == room.Id && t.UserName == alice.Email));
        await ChatTestClient.QuietPeriod();
        Assert.Empty(alice.ReceivedTyping);
        Assert.Empty(eve.ReceivedTyping);
    }

    [Fact]
    public async Task StartTyping_ByNonMember_Fails()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var eve = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => eve.StartTypingAsync(room.Id));

        Assert.Contains("not a member", error.Message);
    }

    [Fact]
    public async Task JoinRoom_InOneTab_SubscribesAllTabs()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var bob = await factory.ConnectNewUserAsync();
        await using var bobSecondTab = await factory.OpenTabAsync(bob);
        var room = await alice.CreateRoomAsync();

        await bob.JoinRoomAsync(room.Id);
        var sent = await alice.SendAsync(room.Id, "hello both tabs");

        await bob.WaitForMessageAsync(m => m.Id == sent.Id);
        await bobSecondTab.WaitForMessageAsync(m => m.Id == sent.Id);
    }

    [Fact]
    public async Task LeaveRoom_InOneTab_UnsubscribesAllTabs()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var bob = await factory.ConnectNewUserAsync();
        await using var bobSecondTab = await factory.OpenTabAsync(bob);
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);

        await bob.LeaveRoomAsync(room.Id);
        await alice.SendAsync(room.Id, "nobody else here");
        await ChatTestClient.QuietPeriod();

        Assert.DoesNotContain(bob.ReceivedMessages, m => m.RoomId == room.Id);
        Assert.DoesNotContain(bobSecondTab.ReceivedMessages, m => m.RoomId == room.Id);
    }
}
