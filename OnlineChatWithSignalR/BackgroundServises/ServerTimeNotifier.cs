using Microsoft.AspNetCore.SignalR;
using OnlineChatWithSignalR.Common;
using OnlineChatWithSignalR.Interfaces;

namespace OnlineChatWithSignalR.BackgroundServises;

public class ServerTimeNotifier(
    ILogger<ServerTimeNotifier> logger,
    IHubContext<NotificationHub, INotificationClient> hubContext)
    : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            var dateTime = DateTime.Now;
            logger.LogInformation($"Executing {nameof(ServerTimeNotifier)} {dateTime}");
           await hubContext.Clients.All.ReceiveNotification($"Server time: {dateTime}");
        }
    }
}