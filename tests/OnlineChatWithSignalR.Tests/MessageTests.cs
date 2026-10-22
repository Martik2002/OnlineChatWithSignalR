using Microsoft.AspNetCore.SignalR;
using OnlineChatWithSignalR.Tests.Infrastructure;

namespace OnlineChatWithSignalR.Tests;

[Collection(ChatApiCollection.Name)]
public sealed class MessageTests(ChatApiFactory factory)
{
    [Fact]
    public async Task SendMessage_IsDeliveredToRoomMembers()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var bob = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);

        var sent = await alice.SendAsync(room.Id, "Hello Bob!");

        var received = await bob.WaitForMessageAsync(m => m.Id == sent.Id);
        Assert.Equal("Hello Bob!", received.Content);
        Assert.Equal(alice.Email, received.SenderName);
        Assert.Equal(room.Id, received.RoomId);
    }

    [Fact]
    public async Task SendMessage_IsNotDeliveredToNonMembers()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var eve = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();

        await alice.SendAsync(room.Id, "secret");
        await ChatTestClient.QuietPeriod();

        Assert.Empty(eve.ReceivedMessages);
    }

    [Fact]
    public async Task SendMessage_ByNonMember_Fails()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var eve = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => eve.SendAsync(room.Id, "let me in"));

        Assert.Contains("not a member", error.Message);
    }

    [Fact]
    public async Task SendMessage_SameIdTwice_IsSavedAndDeliveredOnce()
    {
        // Simulates a client retrying after a network error
        await using var alice = await factory.ConnectNewUserAsync();
        await using var bob = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);
        var messageId = Guid.NewGuid();

        var first = await alice.SendAsync(room.Id, "only once", messageId);
        var second = await alice.SendAsync(room.Id, "only once", messageId);
        await bob.WaitForMessageAsync(m => m.Id == messageId);
        await ChatTestClient.QuietPeriod();

        Assert.Equal(first.Sequence, second.Sequence);
        Assert.Single(bob.ReceivedMessages, m => m.Id == messageId);
        var history = await alice.GetMessagesAsync(room.Id);
        Assert.Single(history.Messages, m => m.Id == messageId);
    }

    [Fact]
    public async Task SendMessage_ConcurrentRetries_CreateOneMessage()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        var messageId = Guid.NewGuid();

        // SignalR runs invocations from one connection one at a time, so a real race
        // needs several connections of the same user (like several open tabs).
        var tabs = new List<ChatTestClient>();
        for (var i = 0; i < 5; i++)
        {
            var tab = new ChatTestClient(
                alice.Email, alice.AccessToken, factory.CreateHubConnection(alice.AccessToken));
            await tab.Connection.StartAsync();
            tabs.Add(tab);
        }

        var results = await Task.WhenAll(tabs.Select(tab => tab.SendAsync(room.Id, "race", messageId)));
        foreach (var tab in tabs)
        {
            await tab.DisposeAsync();
        }

        Assert.Single(results.Select(r => r.Sequence).Distinct());
        var history = await alice.GetMessagesAsync(room.Id);
        Assert.Single(history.Messages, m => m.Id == messageId);
    }

    [Fact]
    public async Task SendMessage_ReusingAnotherUsersMessageId_Fails()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var bob = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);
        var sent = await alice.SendAsync(room.Id, "mine");

        var error = await Assert.ThrowsAsync<HubException>(() => bob.SendAsync(room.Id, "overwrite", sent.Id));

        Assert.Contains("already exists", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendMessage_Empty_Fails(string content)
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();

        await Assert.ThrowsAsync<HubException>(() => alice.SendAsync(room.Id, content));
    }

    [Fact]
    public async Task SendMessage_TooLong_Fails()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => alice.SendAsync(room.Id, new string('a', 2001)));

        Assert.Contains("2000", error.Message);
    }

    [Fact]
    public async Task GetMessages_PagesBackwardsThroughHistory()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        for (var i = 1; i <= 7; i++)
        {
            await alice.SendAsync(room.Id, $"message {i}");
        }

        var latest = await alice.GetMessagesAsync(room.Id, limit: 3);
        var older = await alice.GetMessagesAsync(room.Id, latest.Messages[0].Sequence, limit: 3);
        var oldest = await alice.GetMessagesAsync(room.Id, older.Messages[0].Sequence, limit: 3);

        Assert.Equal(new[] { "message 5", "message 6", "message 7" }, latest.Messages.Select(m => m.Content));
        Assert.True(latest.HasMore);
        Assert.Equal(new[] { "message 2", "message 3", "message 4" }, older.Messages.Select(m => m.Content));
        Assert.True(older.HasMore);
        Assert.Equal(new[] { "message 1" }, oldest.Messages.Select(m => m.Content));
        Assert.False(oldest.HasMore);
    }

    [Fact]
    public async Task GetMessages_ByNonMember_Fails()
    {
        await using var alice = await factory.ConnectNewUserAsync();
        await using var eve = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await alice.SendAsync(room.Id, "private history");

        await Assert.ThrowsAsync<HubException>(() => eve.GetMessagesAsync(room.Id));
    }

    [Fact]
    public async Task Reconnect_RestoresRoomSubscriptions()
    {
        // Group membership is per connection. After reconnecting, the user must still
        // receive messages from rooms they joined, without joining again.
        await using var alice = await factory.ConnectNewUserAsync();
        var bob = await factory.ConnectNewUserAsync();
        var room = await alice.CreateRoomAsync();
        await bob.JoinRoomAsync(room.Id);
        await bob.DisposeAsync();

        await using var bobAgain = new ChatTestClient(
            bob.Email, bob.AccessToken, factory.CreateHubConnection(bob.AccessToken));
        await bobAgain.Connection.StartAsync();

        var sent = await alice.SendAsync(room.Id, "welcome back");

        await bobAgain.WaitForMessageAsync(m => m.Id == sent.Id);
    }
}
