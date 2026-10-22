# OnlineChatWithSignalR

Real-time server-to-client messaging with **ASP.NET Core SignalR** and a **Blazor** client. The server pushes notifications to every connected client without polling, which is the foundation for features like live chat, alerts and dashboards.

## How it works

```
┌──────────────────────┐   WebSocket (SignalR)   ┌───────────────────────┐
│  API (ASP.NET Core)  │ ──────────────────────▶ │  Client (Blazor)      │
│                      │                         │  /notification page   │
│  NotificationHub     │   ReceiveNotification   │  HubConnection        │
│  ServerTimeNotifier  │ ──────────────────────▶ │  renders messages     │
└──────────────────────┘                         └───────────────────────┘
```

- **`NotificationHub`** — a strongly typed hub (`Hub<INotificationClient>`). When a client connects, it receives a personal welcome message through `Clients.Caller`.
- **`INotificationClient`** — the contract of client-side methods, so the server never calls methods by magic string.
- **`ServerTimeNotifier`** — a `BackgroundService` that uses `PeriodicTimer` and `IHubContext` to broadcast the server time to all clients every 5 seconds, showing how to push messages from outside a hub.
- **Client** — a Blazor Server app (interactive server render mode) that connects with `HubConnectionBuilder`, subscribes to `ReceiveNotification` and updates the UI in real time. The connection is disposed with the component.

## Tech Stack

- .NET 9, ASP.NET Core
- SignalR (server + `Microsoft.AspNetCore.SignalR.Client`)
- Blazor (interactive server components)
- Docker / Docker Compose

## Project Structure

```
├── OnlineChatWithSignalR/      # API: hub, client interface, background service
│   ├── Common/NotificationHub.cs
│   ├── Interfaces/INotificationClient.cs
│   └── BackgroundServises/ServerTimeNotifier.cs
├── Client/                     # Blazor client
│   └── Components/Pages/Notification.razor
└── compose.yaml
```

## Running Locally

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

1. Start the API (it listens on `https://localhost:5001`):

   ```bash
   dotnet run --project OnlineChatWithSignalR --launch-profile https
   ```

2. In another terminal, start the client:

   ```bash
   dotnet run --project Client --launch-profile https
   ```

3. Open the client and go to **`/notification`**. You'll get a welcome message, then the server time every 5 seconds.

You can also build both services with Docker Compose:

```bash
docker compose up --build
```

## Possible Next Steps

- Two-way chat: a `SendMessage` hub method and chat rooms with SignalR groups
- Authentication so messages are tied to real users
- Configurable hub URL in the client instead of a hard-coded address
