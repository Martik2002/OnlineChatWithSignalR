using OnlineChatWithSignalR.Chat;

namespace OnlineChatWithSignalR.Hubs;

/// <summary>
/// Methods clients can call on the server. The hub implements this interface and the client
/// uses nameof(IChatHub.X) for method names, so renaming a method breaks the build instead of
/// failing silently at runtime (SignalR method names are just strings on the wire).
/// </summary>
public interface IChatHub
{
    string WhoAmI();
    Task<IReadOnlyList<RoomDto>> GetRooms();
    Task<RoomDto> CreateRoom(string name);
    Task JoinRoom(Guid roomId);
    Task LeaveRoom(Guid roomId);
    Task<MessageDto> SendMessage(SendMessageRequest request);
    Task<MessagePage> GetMessages(GetMessagesRequest request);
    Task<IReadOnlyList<RoomMemberDto>> GetRoomMembers(Guid roomId);
    Task StartTyping(Guid roomId);
}

/// <summary>Methods the server calls on connected clients.</summary>
public interface IChatClient
{
    Task ReceiveSystemMessage(string message);
    Task ReceiveMessage(MessageDto message);
    Task RoomCreated(RoomDto room);
    Task UserJoinedRoom(Guid roomId, string userName);
    Task UserLeftRoom(Guid roomId, string userName);
    Task PresenceChanged(PresenceDto presence);
    Task UserTyping(TypingDto typing);
}
