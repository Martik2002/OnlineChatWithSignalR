namespace OnlineChatWithSignalR.Interfaces;

public interface INotificationClient
{
    Task ReceiveNotification(string message);
}