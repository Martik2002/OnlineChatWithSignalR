using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace OnlineChatWithSignalR.Hubs;

/// <summary>
/// Methods the server can call on connected clients. Using a strongly typed
/// interface means the compiler catches typos in client method names.
/// </summary>
public interface IChatClient
{
    Task ReceiveSystemMessage(string message);
}

[Authorize]
public sealed class ChatHub(ILogger<ChatHub> logger) : Hub<IChatClient>
{
    public const string Path = "/hubs/chat";

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("User {UserId} connected ({ConnectionId})",
            Context.UserIdentifier, Context.ConnectionId);

        await Clients.Caller.ReceiveSystemMessage($"Welcome, {Context.User?.Identity?.Name}!");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("User {UserId} disconnected ({ConnectionId})",
            Context.UserIdentifier, Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Returns the name of the authenticated user. Useful to verify a connection.</summary>
    public string WhoAmI() => Context.User?.Identity?.Name ?? string.Empty;
}
