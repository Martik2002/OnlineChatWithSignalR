# OnlineChatWithSignalR

[![Build](https://github.com/Martik2002/OnlineChatWithSignalR/actions/workflows/build.yml/badge.svg)](https://github.com/Martik2002/OnlineChatWithSignalR/actions/workflows/build.yml)

A real-time chat built with **ASP.NET Core SignalR**, **Blazor** and **PostgreSQL**. Users sign up, create or join rooms, and chat live. Messages are saved, history loads page by page, and the client survives network drops without losing or duplicating messages.

## Features

- 🔐 Accounts with ASP.NET Core Identity; the hub accepts only authenticated connections
- 💬 Chat rooms (SignalR groups) with persistent message history
- ⚡ Optimistic sending: messages appear instantly and are confirmed by the server
- 🔁 Automatic reconnect with catch-up of missed messages and re-sending of unsent ones
- 🧾 **Idempotent sending**: client-generated message ids, so a retry never creates a duplicate
- 📜 Cursor-based paging through history ("load older messages")
- 🧪 Integration tests with real SignalR connections and PostgreSQL (Testcontainers), unit tests for the client logic

## Architecture

```
┌──────────────────────────┐   SignalR (WebSocket)   ┌────────────────────────────┐
│  Client (Blazor Server)  │ ──────────────────────▶ │  API (ASP.NET Core)        │
│                          │   + REST for auth       │                            │
│  Login, rooms, messages  │ ◀────────────────────── │  ChatHub  →  ChatService   │
│  RoomTimeline (merging)  │   pushed messages       │  Identity API endpoints    │
└──────────────────────────┘                         └─────────────┬──────────────┘
              ▲                                                    │ EF Core
              │ shared DTOs + hub interfaces                       ▼
   ┌──────────┴───────────────────┐                        ┌──────────────┐
   │ OnlineChatWithSignalR.       │                        │  PostgreSQL  │
   │ Contracts                    │                        └──────────────┘
   └──────────────────────────────┘
```

| Project | Purpose |
|---------|---------|
| `OnlineChatWithSignalR` | API: Identity endpoints, `ChatHub`, `ChatService`, EF Core and migrations |
| `OnlineChatWithSignalR.Contracts` | DTOs and the `IChatHub` / `IChatClient` interfaces shared by server and client |
| `Client` | Blazor Server UI |
| `tests/OnlineChatWithSignalR.Tests` | Integration tests (real hub connections + PostgreSQL) |
| `tests/Client.Tests` | Unit tests for the client's message merging logic |

## Design Decisions

- **Typed hub contracts.** SignalR method names are strings on the wire. The hub implements `IChatHub` and the client calls `nameof(IChatHub.SendMessage)`, so a renamed method breaks the build instead of failing silently in production.
- **Token in the query string, only for the hub.** Browsers can't set headers on a WebSocket, so SignalR sends `?access_token=`. The API accepts that only for `/hubs/*`; normal endpoints require the `Authorization` header, because URLs end up in logs.
- **Groups are restored on every connect.** SignalR group membership belongs to a connection and is lost on disconnect, so `OnConnectedAsync` re-adds the connection to all of the user's rooms.
- **Idempotent sends.** The client generates the message id. The server returns the already-saved message for a repeated id and doesn't broadcast it again; concurrent duplicates are resolved by the primary key.
- **Keyset paging.** Each message has a database-generated `Sequence`. "50 messages before sequence N" uses an index and stays correct while new messages arrive, unlike `OFFSET` paging.
- **Catch-up after reconnect.** The client fetches the latest page for every open room and merges it. If the page doesn't overlap what it already has, it reloads instead of showing a silent gap.
- **Thin hub, testable logic.** `ChatService` holds the server logic and `RoomTimeline` the client's merge rules, both independent of SignalR and the UI.

## Running Locally

**Requirements:** .NET 9 SDK and PostgreSQL (or Docker).

### Option 1: Docker Compose (everything in containers)

```bash
docker compose up --build
```

Open the client at http://localhost:5062.

### Option 2: Run from your IDE

1. Make sure PostgreSQL is running. The development settings use `postgres` / `postgres` on `localhost:5432`. If your PostgreSQL has a different password, set it with [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (never commit real passwords):

   ```bash
   cd OnlineChatWithSignalR
   dotnet user-secrets set "ConnectionStrings:Chat" "Host=localhost;Port=5432;Database=chat;Username=postgres;Password=<your-password>"
   ```

2. Start the API with the `https` profile (`https://localhost:5001`). The database and tables are created automatically in development.
3. Start the `Client` project and open it in the browser.
4. Register two users in two different browsers (or a private window) and chat between them.

### Tests

Docker must be running: the integration tests start their own PostgreSQL container.

```bash
dotnet test
```

## Roadmap

- [x] Accounts and authenticated hub
- [x] Rooms and persistent messages
- [x] Blazor chat UI
- [ ] Online users and "typing..." indicators (#7)
- [ ] Scale-out with a Redis backplane and multiple API instances (#8)
