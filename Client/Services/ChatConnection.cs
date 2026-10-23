using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using OnlineChatWithSignalR.Chat;
using OnlineChatWithSignalR.Hubs;

namespace Client.Services;

public sealed class ChatApiOptions
{
    public const string Section = "ChatApi";

    public string BaseUrl { get; set; } = "https://localhost:5001";
}

/// <summary>
/// Wraps the SignalR connection to the chat hub for one user circuit.
/// Hub method names come from the shared IChatHub/IChatClient interfaces, so a renamed
/// server method is a compile error here instead of a silent runtime failure.
/// </summary>
public sealed class ChatConnection(
    IOptions<ChatApiOptions> options,
    AuthService auth,
    ILogger<ChatConnection> logger) : IAsyncDisposable
{
    private HubConnection? _hub;

    public HubConnectionState State => _hub?.State ?? HubConnectionState.Disconnected;

    public event Func<MessageDto, Task>? MessageReceived;
    public event Func<RoomDto, Task>? RoomCreated;
    public event Func<Guid, string, Task>? MembershipChanged;
    public event Func<Task>? StateChanged;
    public event Func<PresenceDto, Task>? PresenceChanged;
    public event Func<TypingDto, Task>? UserTyping;

    /// <summary>Raised after an automatic reconnect: the client should catch up on missed messages.</summary>
    public event Func<Task>? Reconnected;

    public async Task StartAsync()
    {
        if (_hub is not null)
        {
            return;
        }

        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(options.Value.BaseUrl), "hubs/chat"), connection =>
            {
                // Called on every connect and reconnect, so a refreshed token is always used
                connection.AccessTokenProvider = auth.GetAccessTokenAsync;

                // Normally SignalR first calls /negotiate and then opens the connection, and both
                // requests must reach the same server ("sticky sessions" on the load balancer).
                // Going straight to WebSockets is a single request, so any server can take it.
                connection.SkipNegotiation = true;
                connection.Transports = HttpTransportType.WebSockets;
            })
            .WithAutomaticReconnect(new KeepRetryingPolicy())
            .Build();

        _hub.On<MessageDto>(nameof(IChatClient.ReceiveMessage), message => RaiseAsync(MessageReceived, message));
        _hub.On<RoomDto>(nameof(IChatClient.RoomCreated), room => RaiseAsync(RoomCreated, room));
        _hub.On<Guid, string>(nameof(IChatClient.UserJoinedRoom), (roomId, user) => RaiseAsync(MembershipChanged, roomId, user));
        _hub.On<Guid, string>(nameof(IChatClient.UserLeftRoom), (roomId, user) => RaiseAsync(MembershipChanged, roomId, user));
        _hub.On<PresenceDto>(nameof(IChatClient.PresenceChanged), presence => RaiseAsync(PresenceChanged, presence));
        _hub.On<TypingDto>(nameof(IChatClient.UserTyping), typing => RaiseAsync(UserTyping, typing));

        _hub.Reconnecting += _ => RaiseAsync(StateChanged);
        _hub.Reconnected += async _ =>
        {
            logger.LogInformation("Reconnected to the chat hub");
            await RaiseAsync(StateChanged);
            await RaiseAsync(Reconnected);
        };
        _hub.Closed += _ => RaiseAsync(StateChanged);

        await _hub.StartAsync();
        await RaiseAsync(StateChanged);
    }

    public Task<IReadOnlyList<RoomDto>> GetRoomsAsync() =>
        Hub.InvokeAsync<IReadOnlyList<RoomDto>>(nameof(IChatHub.GetRooms));

    public Task<RoomDto> CreateRoomAsync(string name) =>
        Hub.InvokeAsync<RoomDto>(nameof(IChatHub.CreateRoom), name);

    public Task JoinRoomAsync(Guid roomId) =>
        Hub.InvokeAsync(nameof(IChatHub.JoinRoom), roomId);

    public Task LeaveRoomAsync(Guid roomId) =>
        Hub.InvokeAsync(nameof(IChatHub.LeaveRoom), roomId);

    public Task<MessageDto> SendMessageAsync(SendMessageRequest request) =>
        Hub.InvokeAsync<MessageDto>(nameof(IChatHub.SendMessage), request);

    public Task<MessagePage> GetMessagesAsync(GetMessagesRequest request) =>
        Hub.InvokeAsync<MessagePage>(nameof(IChatHub.GetMessages), request);

    public Task<IReadOnlyList<RoomMemberDto>> GetRoomMembersAsync(Guid roomId) =>
        Hub.InvokeAsync<IReadOnlyList<RoomMemberDto>>(nameof(IChatHub.GetRoomMembers), roomId);

    /// <summary>
    /// Fire-and-forget: SendAsync doesn't wait for the server's result, which is fine for a
    /// typing signal (losing one is harmless) and keeps typing in the input box responsive.
    /// </summary>
    public Task StartTypingAsync(Guid roomId) =>
        Hub.SendAsync(nameof(IChatHub.StartTyping), roomId);

    /// <summary>Closes the connection (on logout). StartAsync can be called again afterwards.</summary>
    public async Task StopAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
        }
    }

    private HubConnection Hub => _hub ?? throw new InvalidOperationException("Call StartAsync first.");

    private async Task RaiseAsync(Func<Task>? handler)
    {
        if (handler is not null)
        {
            await handler();
        }
    }

    private async Task RaiseAsync<T>(Func<T, Task>? handler, T arg)
    {
        if (handler is not null)
        {
            await handler(arg);
        }
    }

    private async Task RaiseAsync<T1, T2>(Func<T1, T2, Task>? handler, T1 arg1, T2 arg2)
    {
        if (handler is not null)
        {
            await handler(arg1, arg2);
        }
    }

    /// <summary>
    /// The default policy gives up after about 45 seconds. A chat should keep trying (for example
    /// after a laptop wakes up), with growing delays so a down server isn't flooded.
    /// </summary>
    private sealed class KeepRetryingPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
        [
            TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)
        ];

        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            retryContext.PreviousRetryCount < Delays.Length
                ? Delays[retryContext.PreviousRetryCount]
                : TimeSpan.FromSeconds(30);
    }
}
