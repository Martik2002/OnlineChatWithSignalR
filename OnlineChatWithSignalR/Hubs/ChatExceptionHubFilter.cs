using Microsoft.AspNetCore.SignalR;
using OnlineChatWithSignalR.Chat;

namespace OnlineChatWithSignalR.Hubs;

/// <summary>
/// SignalR hides exception messages from clients by default (they may contain internal details).
/// Expected errors are thrown as <see cref="ChatException"/>; this filter turns them into a
/// <see cref="HubException"/>, whose message is sent to the client.
/// </summary>
public sealed class ChatExceptionHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (ChatException ex)
        {
            throw new HubException(ex.Message);
        }
    }
}
