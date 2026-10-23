# OnlineChatWithSignalR

[![Build](https://github.com/Martik2002/OnlineChatWithSignalR/actions/workflows/build.yml/badge.svg)](https://github.com/Martik2002/OnlineChatWithSignalR/actions/workflows/build.yml)

A real-time chat built with **ASP.NET Core SignalR**, **Blazor**, **PostgreSQL** and **Redis**. Users sign up, create or join rooms, and chat live. Messages are saved, history loads page by page, the client survives network drops without losing or duplicating messages, and the API scales out to several instances behind a load balancer.

## Features

- 🔐 Accounts with ASP.NET Core Identity; the hub accepts only authenticated connections
- 💬 Chat rooms (SignalR groups) with persistent message history
- ⚡ Optimistic sending: messages appear instantly and are confirmed by the server
- 🔁 Automatic reconnect with catch-up of missed messages and re-sending of unsent ones
- 🟢 Online members per room and "is typing..." indicators
- 🧾 **Idempotent sending**: client-generated message ids, so a retry never creates a duplicate
- 📜 Cursor-based paging through history ("load older messages")
- 🌐 **Horizontal scale-out**: several API instances behind a load balancer, connected through a Redis backplane
- 🛡️ Per-user message rate limiting
- 🧪 Integration tests with real SignalR connections, PostgreSQL and Redis (Testcontainers), including two API instances in one test

## Architecture

```
                                                     ┌───────────────────────────┐
┌─────────────────────────┐    ┌────────────────┐    │  API instance 1           │
│  Client (Blazor Server) │    │                │ ──▶│  ChatHub → ChatService    │──┐
│                         │───▶│     nginx      │    └─────────────┬─────────────┘  │
│  Login, rooms, messages │ WS │  round robin   │                  │ pub/sub        │ EF Core
│  RoomTimeline, typing   │    │                │    ┌─────────────▼─────────────┐  │
└─────────────────────────┘    │                │ ──▶│  Redis                    │  │
                               └────────────────┘    │  backplane · presence ·   │  │
                                        │            │  Data Protection keys     │  │
                                        │            └─────────────▲─────────────┘  │
                                        │                          │ pub/sub        ▼
                                        │            ┌─────────────┴─────────────┐ ┌────────────┐
                                        └───────────▶│  API instance 2           │▶│ PostgreSQL │
                                                     └───────────────────────────┘ └────────────┘
```

| Project | Purpose |
|---------|---------|
| `OnlineChatWithSignalR` | API: Identity endpoints, `ChatHub`, `ChatService`, EF Core and migrations |
| `OnlineChatWithSignalR.Contracts` | DTOs and the `IChatHub` / `IChatClient` interfaces shared by server and client |
| `Client` | Blazor Server UI |
| `tests/OnlineChatWithSignalR.Tests` | Integration tests (real hub connections, PostgreSQL, Redis, two API instances) |
| `deploy/nginx.conf` | Load balancer for the two API instances in Docker Compose |
| `tests/Client.Tests` | Unit tests for the client's message merging logic |

## Design Decisions

- **Typed hub contracts.** SignalR method names are strings on the wire. The hub implements `IChatHub` and the client calls `nameof(IChatHub.SendMessage)`, so a renamed method breaks the build instead of failing silently in production.
- **Token in the query string, only for the hub.** Browsers can't set headers on a WebSocket, so SignalR sends `?access_token=`. The API accepts that only for `/hubs/*`; normal endpoints require the `Authorization` header, because URLs end up in logs.
- **Groups are restored on every connect.** SignalR group membership belongs to a connection and is lost on disconnect, so `OnConnectedAsync` re-adds the connection to all of the user's rooms.
- **Idempotent sends.** The client generates the message id. The server returns the already-saved message for a repeated id and doesn't broadcast it again; concurrent duplicates are resolved by the primary key.
- **Keyset paging.** Each message has a database-generated `Sequence`. "50 messages before sequence N" uses an index and stays correct while new messages arrive, unlike `OFFSET` paging.
- **Catch-up after reconnect.** The client fetches the latest page for every open room and merges it. If the page doesn't overlap what it already has, it reloads instead of showing a silent gap.
- **Presence counts connections, not users.** A user is online while at least one tab or device is connected, so closing one of several tabs doesn't mark them offline. Presence changes are only sent to users who share a room with them.
- **Typing signals expire instead of being cancelled.** The client sends at most one "typing" signal every 2 seconds, and receivers hide it after 4 seconds without a new one. There is no "stopped typing" message that could get lost and leave the indicator stuck.
- **Room membership applies to all tabs.** Joining or leaving a room in one tab updates the SignalR groups of all the user's connections.
- **Scale-out with a Redis backplane.** Each API instance only holds its own connections. The backplane publishes every message, group change and user message through Redis, so a message sent on instance 1 reaches users connected to instance 2.
- **Shared Data Protection keys.** Identity bearer tokens are encrypted with Data Protection keys. With per-server keys, a token issued by one instance is rejected by the others, so the keys are stored in Redis.
- **No sticky sessions.** SignalR normally sends a `negotiate` request first, and the real connection must reach the same server. The client goes straight to WebSockets (`SkipNegotiation`), a single request that any instance can accept, so the load balancer can use plain round robin.
- **Presence in Redis with heartbeats.** Connections are stored per user in a Redis sorted set, scored by "last seen". Each instance refreshes its connections every 30 seconds, and entries older than 90 seconds are ignored, so users of a crashed instance don't stay "online" forever. Adding a connection and counting the rest runs as one atomic Lua script.
- **Rate limiting per user, per instance.** A sliding window limits messages per user (20 per 10 seconds). HTTP rate limiting can't do this: a SignalR connection is one long request with many invocations inside. The counters are kept in memory, so with N instances the effective limit is up to N times higher, an accepted trade-off to avoid a Redis round trip per message.
- **One migrator.** Only one instance applies database migrations at startup, so two instances never race to migrate.
- **Thin hub, testable logic.** `ChatService` holds the server logic and `RoomTimeline` the client's merge rules, both independent of SignalR and the UI.

## Running Locally

**Requirements:** .NET 9 SDK and PostgreSQL (or Docker). Redis is optional: without `ConnectionStrings:Redis` the API runs as a single instance with in-memory presence.

### Option 1: Docker Compose (two API instances, like production)

```bash
docker compose up --build
```

This starts PostgreSQL, Redis, **two API instances** (`api-1`, `api-2`), an nginx load balancer and the client. Open the client at http://localhost:5062, then sign in as two users in two browsers: their connections are spread over both instances by the load balancer, and messages still reach everyone.

To see the scale-out in action, stop one instance with `docker compose stop api-2`. Users connected to it reconnect automatically to `api-1`, and the chat keeps working.

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

Docker must be running: the integration tests start their own PostgreSQL and Redis containers.

```bash
dotnet test
```

## Roadmap

- [x] Accounts and authenticated hub
- [x] Rooms and persistent messages
- [x] Blazor chat UI
- [x] Online users and "typing..." indicators
- [x] Scale-out with a Redis backplane and multiple API instances
