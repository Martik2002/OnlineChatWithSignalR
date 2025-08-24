using Microsoft.AspNetCore.SignalR;
using OnlineChatWithSignalR.Interfaces;

namespace OnlineChatWithSignalR.Common;

public class NotificationHub : Hub<INotificationClient>
{
    public override async Task OnConnectedAsync()
    {
         await Clients.Clients(Context.ConnectionId).ReceiveNotification(
            $"Thank you connecting {Context.User?.Identity?.Name}");
         await base.OnConnectedAsync();
    }
}