using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OnlineChatWithSignalR.Chat;

namespace OnlineChatWithSignalR.Hubs;

[Authorize]
public sealed class ChatHub(ChatService chat, ILogger<ChatHub> logger) : Hub<IChatClient>, IChatHub
{
    public const string Path = "/hubs/chat";

    public static string RoomGroup(Guid roomId) => $"room:{roomId}";

    private CurrentUser User => new(
        Context.UserIdentifier ?? throw new HubException("Not authenticated."),
        Context.User?.Identity?.Name ?? string.Empty);

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("User {UserId} connected ({ConnectionId})",
            Context.UserIdentifier, Context.ConnectionId);

        // Group membership belongs to a connection, not to a user, and is lost when the
        // connection closes. So on every (re)connect, add this connection to all rooms
        // the user is a member of.
        var roomIds = await chat.GetRoomIdsOfUserAsync(User.Id, Context.ConnectionAborted);
        foreach (var roomId in roomIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId), Context.ConnectionAborted);
        }

        await Clients.Caller.ReceiveSystemMessage($"Welcome, {User.Name}!");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("User {UserId} disconnected ({ConnectionId})",
            Context.UserIdentifier, Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Returns the name of the authenticated user. Useful to verify a connection.</summary>
    public string WhoAmI() => User.Name;

    public Task<IReadOnlyList<RoomDto>> GetRooms() =>
        chat.GetRoomsAsync(User, Context.ConnectionAborted);

    public async Task<RoomDto> CreateRoom(string name)
    {
        var room = await chat.CreateRoomAsync(User, name, Context.ConnectionAborted);

        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(room.Id));
        // Everyone else sees the new room in their list (as not joined)
        await Clients.Others.RoomCreated(room with { IsMember = false });

        return room;
    }

    public async Task JoinRoom(Guid roomId)
    {
        var joined = await chat.JoinRoomAsync(User, roomId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId));

        if (joined)
        {
            await Clients.OthersInGroup(RoomGroup(roomId)).UserJoinedRoom(roomId, User.Name);
        }
    }

    public async Task LeaveRoom(Guid roomId)
    {
        var left = await chat.LeaveRoomAsync(User, roomId, Context.ConnectionAborted);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup(roomId));

        if (left)
        {
            await Clients.Group(RoomGroup(roomId)).UserLeftRoom(roomId, User.Name);
        }
    }

    public async Task<MessageDto> SendMessage(SendMessageRequest request)
    {
        var (message, isNew) = await chat.SendMessageAsync(User, request, Context.ConnectionAborted);

        // A retried send returns the saved message without broadcasting it again
        if (isNew)
        {
            await Clients.Group(RoomGroup(message.RoomId)).ReceiveMessage(message);
        }

        return message;
    }

    public Task<MessagePage> GetMessages(GetMessagesRequest request) =>
        chat.GetMessagesAsync(User, request, Context.ConnectionAborted);
}
