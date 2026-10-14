# OpenRec-2

OpenRec-2 is a modular C# server backend for legacy Rec Room-compatible clients.

## Current capabilities

OpenRec-2 provides configurable server identity, supported client version, region, CDN URI, request size, and CORS. It includes health and readiness checks, runtime status, Prometheus-compatible metrics, request correlation IDs, structured JSON errors, and compatibility dispatch for the existing legacy API surface.

The server now also provides account registration and login with salted PBKDF2 password hashing, expiring bearer sessions, logout, and authenticated identity lookup. Room management supports public or private rooms, activity and region filtering, capacity limits, membership tracking, join and leave operations, and owner-only deletion.

## Run

```bash
dotnet run --urls http://localhost:5229
```

The service exposes `GET /health`, `GET /ready`, `GET /api/v1/status`, and `GET /metrics`. Authentication is available under `/api/v1/auth`, and room management is available under `/api/v1/rooms`.

The project targets .NET 10 and reads server settings from `appsettings.json`. The current account and room stores are process-local and should be replaced with a durable database before production deployment.
