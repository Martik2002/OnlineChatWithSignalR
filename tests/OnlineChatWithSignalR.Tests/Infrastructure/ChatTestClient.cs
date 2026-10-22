using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using OnlineChatWithSignalR.Chat;

namespace OnlineChatWithSignalR.Tests.Infrastructure;

/// <summary>
/// A test user with a live hub connection. Records every message pushed by the server,
/// so tests can assert what each user received.
/// </summary>
public sealed class ChatTestClient : IAsyncDisposable
{
    private readonly ConcurrentQueue<MessageDto> _received = new();

    public ChatTestClient(string email, string accessToken, HubConnection connection)
    {
        Email = email;
        AccessToken = accessToken;
        Connection = connection;
        Connection.On<MessageDto>("ReceiveMessage", message => _received.Enqueue(message));
    }

    public string Email { get; }
    public string AccessToken { get; }
    public HubConnection Connection { get; }

    public IReadOnlyList<MessageDto> ReceivedMessages => _received.ToList();

    public Task<RoomDto> CreateRoomAsync(string? name = null) =>
        Connection.InvokeAsync<RoomDto>("CreateRoom", name ?? $"room-{Guid.NewGuid():N}");

    public Task JoinRoomAsync(Guid roomId) => Connection.InvokeAsync("JoinRoom", roomId);

    public Task LeaveRoomAsync(Guid roomId) => Connection.InvokeAsync("LeaveRoom", roomId);

    public Task<MessageDto> SendAsync(Guid roomId, string content, Guid? messageId = null) =>
        Connection.InvokeAsync<MessageDto>(
            "SendMessage", new SendMessageRequest(messageId ?? Guid.NewGuid(), roomId, content));

    public Task<MessagePage> GetMessagesAsync(Guid roomId, long? beforeSequence = null, int limit = 50) =>
        Connection.InvokeAsync<MessagePage>(
            "GetMessages", new GetMessagesRequest(roomId, beforeSequence, limit));

    /// <summary>Waits until a pushed message matches, or fails after a timeout.</summary>
    public async Task<MessageDto> WaitForMessageAsync(Func<MessageDto, bool> match, int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var message = _received.FirstOrDefault(match);
            if (message is not null)
            {
                return message;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Expected message was not received.");
    }

    /// <summary>
    /// Proving that something did NOT arrive needs a short wait. Long polling delivers
    /// pushed messages within milliseconds in the test server, so one second is plenty.
    /// </summary>
    public static Task QuietPeriod() => Task.Delay(TimeSpan.FromSeconds(1));

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
