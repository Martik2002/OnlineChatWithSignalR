using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OnlineChatWithSignalR.Chat;
using OnlineChatWithSignalR.Tests.Infrastructure;

namespace OnlineChatWithSignalR.Tests;

[Collection(ChatApiCollection.Name)]
public sealed class RoomTests(ChatApiFactory factory)
{
    [Fact]
    public async Task CreateRoom_CreatorBecomesMember()
    {
        await using var alice = await factory.ConnectNewUserAsync();

        var room = await alice.CreateRoomAsync();
        var rooms = await alice.Connection.InvokeAsync<List<RoomDto>>("GetRooms");

        Assert.True(room.IsMember);
        Assert.Equal(1, room.MemberCount);
        Assert.Contains(rooms, r => r.Id == room.Id && r.IsMember);
    }

    [Fact]
    public async Task CreateRoom_WithDuplicateName_Fails()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var name = $"general-{Guid.NewGuid():N}";
        await alice.CreateRoomAsync(name);

        var error = await Assert.ThrowsAsync<HubException>(() => alice.CreateRoomAsync(name));

        Assert.Contains("already exists", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateRoom_WithEmptyName_Fails(string name)
    {
        await using var alice = await factory.ConnectNewUserAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => alice.CreateRoomAsync(name));

        Assert.Contains("Room name", error.Message);
    }

    [Fact]
    public async Task JoinRoom_UnknownRoom_Fails()
    {
        await using var alice = await factory.ConnectNewUserAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => alice.JoinRoomAsync(Guid.NewGuid()));

        Assert.Contains("not found", error.Message);
    }

    [Fact]
    public async Task JoinRoom_IsIdempotent()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var bob = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();

        await bob.JoinRoomAsync(room.Id);
        await bob.JoinRoomAsync(room.Id);

        var rooms = await bob.Connection.InvokeAsync<List<RoomDto>>("GetRooms");
        Assert.Equal(2, rooms.Single(r => r.Id == room.Id).MemberCount);
    }

    [Fact]
    public async Task LeaveRoom_StopsMessagesAndBlocksSending()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var bob = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);

        await bob.LeaveRoomAsync(room.Id);
        await alice.SendAsync(room.Id, "are you there?");
        await ChatTestClient.QuietPeriod();

        Assert.DoesNotContain(bob.ReceivedMessages, m => m.RoomId == room.Id);
        await Assert.ThrowsAsync<HubException>(() => bob.SendAsync(room.Id, "hello"));
    }
}
