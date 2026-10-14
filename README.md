# OpenRec-2

OpenRec-2 is a modular C# server backend for legacy Rec Room-compatible clients.

## Current capabilities

OpenRec-2 provides configurable server identity, supported client version, region, CDN URI, request size, and CORS. It includes health and readiness checks, runtime status, Prometheus-compatible metrics, request correlation IDs, structured JSON errors, and compatibility dispatch for the existing legacy API surface.

The server provides account registration and login with salted PBKDF2 password hashing, expiring bearer sessions, logout, and authenticated identity lookup. Room management supports public or private rooms, activity and region filtering, capacity limits, membership tracking, join and leave operations, owner-only deletion, room chat history, and owner-controlled room customization. Customization supports display names, descriptions, themes, tags, welcome messages, validation, and revision numbers.

Social features include friend requests, acceptance, decline, removal, and friend-list retrieval. Moderation features include user reports, configurable moderator roles, temporary or permanent bans, unbanning, moderator report and ban queues, blocked-word chat filtering, and chat restrictions for banned users.

Player inventories support item grants, quantities, categories, metadata, equipped state, inventory listing, quantity updates, and moderator-controlled grants.

## Run

```bash
dotnet run --urls http://localhost:5229
```

The service exposes `GET /health`, `GET /ready`, `GET /api/v1/status`, and `GET /metrics`. Authentication is available under `/api/v1/auth`, room management and chat are available under `/api/v1/rooms`, room customization is available under `/api/v1/rooms/{id}/customization`, friend features are available under `/api/v1/friends`, moderation is available under `/api/v1/moderation`, and inventories are available under `/api/v1/inventory`.

Set `Server:ModeratorUsernames` and `Server:BlockedChatWords` in `appsettings.json` to configure moderation. The current account, session, room, customization, inventory, social, and moderation stores are process-local and should be replaced with durable storage before production deployment.

The project targets .NET 10.
