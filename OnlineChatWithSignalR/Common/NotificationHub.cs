using Microsoft.AspNetCore.SignalR;
using OnlineChatWithSignalR.Interfaces;

namespace OnlineChatWithSignalR.Common;

public class NotificationHub : Hub<INotificationClient>
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.ReceiveNotification(
            $"Thank you for connecting {Context.User?.Identity?.Name}".TrimEnd());
         await base.OnConnectedAsync();
    }
}