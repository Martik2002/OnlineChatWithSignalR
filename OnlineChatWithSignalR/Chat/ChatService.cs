using Microsoft.EntityFrameworkCore;
using Npgsql;
using OnlineChatWithSignalR.Data;

namespace OnlineChatWithSignalR.Chat;

/// <summary>
/// Chat use cases. Kept separate from the hub so the logic does not depend on SignalR
/// and the hub only deals with connections and groups.
/// </summary>
public sealed class ChatService(ChatDbContext db, TimeProvider timeProvider)
{
    public const int MaxPageSize = 100;

    public async Task<IReadOnlyList<RoomDto>> GetRoomsAsync(CurrentUser user, CancellationToken ct = default)
    {
        return await db.Rooms
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoomDto(
                r.Id,
                r.Name,
                r.Members.Count,
                r.Members.Any(m => m.UserId == user.Id)))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetRoomIdsOfUserAsync(string userId, CancellationToken ct = default)
    {
        return await db.RoomMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.RoomId)
            .ToListAsync(ct);
    }

    public async Task<RoomDto> CreateRoomAsync(CurrentUser user, string name, CancellationToken ct = default)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length is 0 or > ChatRoom.NameMaxLength)
        {
            throw new ChatException($"Room name must be between 1 and {ChatRoom.NameMaxLength} characters.");
        }

        if (await db.Rooms.AnyAsync(r => r.Name == name, ct))
        {
            throw new ChatException($"A room named '{name}' already exists.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var room = new ChatRoom
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedByUserId = user.Id,
            CreatedAt = now
        };
        room.Members.Add(new RoomMember { RoomId = room.Id, UserId = user.Id, JoinedAt = now });
        db.Rooms.Add(room);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another user created a room with the same name between our check and the insert
            throw new ChatException($"A room named '{name}' already exists.");
        }

        return new RoomDto(room.Id, room.Name, MemberCount: 1, IsMember: true);
    }

    /// <returns>True if the user was not a member before.</returns>
    public async Task<bool> JoinRoomAsync(CurrentUser user, Guid roomId, CancellationToken ct = default)
    {
        if (!await db.Rooms.AnyAsync(r => r.Id == roomId, ct))
        {
            throw new ChatException("Room not found.");
        }

        if (await IsMemberAsync(user.Id, roomId, ct))
        {
            return false;
        }

        db.RoomMembers.Add(new RoomMember
        {
            RoomId = roomId,
            UserId = user.Id,
            JoinedAt = timeProvider.GetUtcNow().UtcDateTime
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Joined at the same time from another tab
            return false;
        }
    }

    /// <returns>True if the user was a member.</returns>
    public async Task<bool> LeaveRoomAsync(CurrentUser user, Guid roomId, CancellationToken ct = default)
    {
        var deleted = await db.RoomMembers
            .Where(m => m.RoomId == roomId && m.UserId == user.Id)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }

    /// <summary>
    /// Saves a message. Idempotent: if a message with the same id was already saved (for example
    /// the client retried after a network error), the saved message is returned and IsNew is false,
    /// so the caller does not broadcast it twice.
    /// </summary>
    public async Task<(MessageDto Message, bool IsNew)> SendMessageAsync(
        CurrentUser user, SendMessageRequest request, CancellationToken ct = default)
    {
        if (request.MessageId == Guid.Empty)
        {
            throw new ChatException("Message id is required.");
        }

        var content = request.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ChatException("Message can't be empty.");
        }

        if (content.Length > ChatMessage.ContentMaxLength)
        {
            throw new ChatException($"Message can't be longer than {ChatMessage.ContentMaxLength} characters.");
        }

        var existing = await FindMessageAsync(request.MessageId, ct);
        if (existing is not null)
        {
            return (EnsureSameMessage(existing, user, request), false);
        }

        await EnsureMemberAsync(user.Id, request.RoomId, ct);

        var message = new ChatMessage
        {
            Id = request.MessageId,
            RoomId = request.RoomId,
            SenderId = user.Id,
            Content = content,
            SentAt = timeProvider.GetUtcNow().UtcDateTime
        };
        db.Messages.Add(message);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The same message was sent twice at the same time and the other request won
            db.Entry(message).State = EntityState.Detached;
            var saved = await FindMessageAsync(request.MessageId, ct)
                ?? throw new InvalidOperationException("Message disappeared after a unique violation.");
            return (EnsureSameMessage(saved, user, request), false);
        }

        return (new MessageDto(
            message.Id, message.Sequence, message.RoomId, user.Id, user.Name, message.Content, message.SentAt), true);
    }

    public async Task<MessagePage> GetMessagesAsync(
        CurrentUser user, GetMessagesRequest request, CancellationToken ct = default)
    {
        await EnsureMemberAsync(user.Id, request.RoomId, ct);

        var limit = Math.Clamp(request.Limit, 1, MaxPageSize);

        var query = db.Messages
            .AsNoTracking()
            .Where(m => m.RoomId == request.RoomId);

        if (request.BeforeSequence is { } before)
        {
            query = query.Where(m => m.Sequence < before);
        }

        // Keyset ("cursor") paging: uses the (RoomId, Sequence) index and stays correct while
        // new messages arrive, unlike OFFSET-based paging. One extra row tells us if there is more.
        var messages = await query
            .OrderByDescending(m => m.Sequence)
            .Take(limit + 1)
            .Select(ToDto)
            .ToListAsync(ct);

        var hasMore = messages.Count > limit;
        var page = messages.Take(limit).Reverse().ToList();

        return new MessagePage(page, hasMore);
    }

    public async Task<IReadOnlyList<RoomMemberInfo>> GetMembersAsync(
        CurrentUser user, Guid roomId, CancellationToken ct = default)
    {
        await EnsureMemberAsync(user.Id, roomId, ct);

        return await db.RoomMembers
            .AsNoTracking()
            .Where(m => m.RoomId == roomId)
            .Select(m => new RoomMemberInfo(m.UserId, m.User.UserName!))
            .ToListAsync(ct);
    }

    public Task<bool> IsMemberAsync(string userId, Guid roomId, CancellationToken ct = default) =>
        db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == userId, ct);

    private async Task EnsureMemberAsync(string userId, Guid roomId, CancellationToken ct)
    {
        if (!await IsMemberAsync(userId, roomId, ct))
        {
            throw new ChatException("You are not a member of this room.");
        }
    }

    private Task<MessageDto?> FindMessageAsync(Guid messageId, CancellationToken ct) =>
        db.Messages
            .AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(ToDto)
            .FirstOrDefaultAsync(ct);

    private static MessageDto EnsureSameMessage(MessageDto saved, CurrentUser user, SendMessageRequest request)
    {
        // A message id can only be "retried" by the same user for the same room,
        // otherwise someone could probe or overwrite other users' messages.
        if (saved.SenderId != user.Id || saved.RoomId != request.RoomId)
        {
            throw new ChatException("A message with this id already exists.");
        }

        return saved;
    }

    private static readonly System.Linq.Expressions.Expression<Func<ChatMessage, MessageDto>> ToDto =
        m => new MessageDto(m.Id, m.Sequence, m.RoomId, m.SenderId, m.Sender.UserName!, m.Content, m.SentAt);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
