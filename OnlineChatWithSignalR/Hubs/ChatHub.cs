using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OnlineChatWithSignalR.Chat;

namespace OnlineChatWithSignalR.Hubs;

[Authorize]
public sealed class ChatHub(ChatService chat, PresenceTracker presence, ILogger<ChatHub> logger)
    : Hub<IChatClient>, IChatHub
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

        // Track the connection before adding groups: a JoinRoom from another tab running at the
        // same time then already sees this connection and adds it to the new room too.
        var cameOnline = presence.Connected(User.Id, Context.ConnectionId);

        // Group membership belongs to a connection, not to a user, and is lost when the
        // connection closes. So on every (re)connect, add this connection to all rooms
        // the user is a member of.
        var roomIds = await chat.GetRoomIdsOfUserAsync(User.Id, Context.ConnectionAborted);
        foreach (var roomId in roomIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId), Context.ConnectionAborted);
        }

        if (cameOnline)
        {
            await NotifyPresenceAsync(roomIds, isOnline: true);
        }

        await Clients.Caller.ReceiveSystemMessage($"Welcome, {User.Name}!");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("User {UserId} disconnected ({ConnectionId})",
            Context.UserIdentifier, Context.ConnectionId);

        // Offline only when the last tab/device disconnects
        if (presence.Disconnected(User.Id, Context.ConnectionId))
        {
            // ConnectionAborted is already cancelled at this point, so don't pass it
            var roomIds = await chat.GetRoomIdsOfUserAsync(User.Id, CancellationToken.None);
            await NotifyPresenceAsync(roomIds, isOnline: false);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Returns the name of the authenticated user. Useful to verify a connection.</summary>
    public string WhoAmI() => User.Name;

    public Task<IReadOnlyList<RoomDto>> GetRooms() =>
        chat.GetRoomsAsync(User, Context.ConnectionAborted);

    public async Task<RoomDto> CreateRoom(string name)
    {
        var room = await chat.CreateRoomAsync(User, name, Context.ConnectionAborted);

        await AddUserConnectionsToGroupAsync(room.Id);
        // Everyone else sees the new room in their list (as not joined)
        await Clients.Others.RoomCreated(room with { IsMember = false });

        return room;
    }

    public async Task JoinRoom(Guid roomId)
    {
        var joined = await chat.JoinRoomAsync(User, roomId, Context.ConnectionAborted);

        // All of the user's tabs start receiving the room's messages, not only this one
        await AddUserConnectionsToGroupAsync(roomId);

        if (joined)
        {
            // Also reaches the user's other tabs, so they refresh their room list
            await Clients.OthersInGroup(RoomGroup(roomId)).UserJoinedRoom(roomId, User.Name);
        }
    }

    public async Task LeaveRoom(Guid roomId)
    {
        var left = await chat.LeaveRoomAsync(User, roomId, Context.ConnectionAborted);

        foreach (var connectionId in ConnectionsOfCurrentUser())
        {
            await Groups.RemoveFromGroupAsync(connectionId, RoomGroup(roomId));
        }

        if (left)
        {
            await Clients.Group(RoomGroup(roomId)).UserLeftRoom(roomId, User.Name);
            // The user's own tabs are no longer in the group, notify them directly
            await Clients.User(User.Id).UserLeftRoom(roomId, User.Name);
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

    public async Task<IReadOnlyList<RoomMemberDto>> GetRoomMembers(Guid roomId)
    {
        var members = await chat.GetMembersAsync(User, roomId, Context.ConnectionAborted);

        return members
            .Select(m => new RoomMemberDto(m.UserId, m.UserName, presence.IsOnline(m.UserId)))
            .OrderByDescending(m => m.IsOnline)
            .ThenBy(m => m.UserName)
            .ToList();
    }

    /// <summary>
    /// Clients call this at most every few seconds while the user types (throttled on the client).
    /// The membership check stops non-members from broadcasting into a room's group.
    /// </summary>
    public async Task StartTyping(Guid roomId)
    {
        if (!await chat.IsMemberAsync(User.Id, roomId, Context.ConnectionAborted))
        {
            throw new ChatException("You are not a member of this room.");
        }

        await Clients.OthersInGroup(RoomGroup(roomId)).UserTyping(new TypingDto(roomId, User.Id, User.Name));
    }

    private async Task NotifyPresenceAsync(IReadOnlyList<Guid> roomIds, bool isOnline)
    {
        if (roomIds.Count == 0)
        {
            return;
        }

        // Only users who share a room with this user are told about it
        var groups = roomIds.Select(RoomGroup).ToList();
        await Clients.Groups(groups).PresenceChanged(new PresenceDto(User.Id, User.Name, isOnline));
    }

    private async Task AddUserConnectionsToGroupAsync(Guid roomId)
    {
        foreach (var connectionId in ConnectionsOfCurrentUser())
        {
            await Groups.AddToGroupAsync(connectionId, RoomGroup(roomId));
        }
    }

    private IEnumerable<string> ConnectionsOfCurrentUser() =>
        presence.GetConnections(User.Id).Append(Context.ConnectionId).Distinct();
}
