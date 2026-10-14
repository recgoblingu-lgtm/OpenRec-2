# OpenRec-2

OpenRec-2 is a modular C# server backend for legacy Rec Room-compatible clients.

## Current capabilities

- Configurable server identity, supported client version, region, CDN URI, request size, and CORS
- Health and readiness endpoints
- Runtime status endpoint with uptime, request, error, and concurrency metrics
- Prometheus-compatible text metrics
- Request correlation IDs through `X-Request-Id`
- Structured JSON errors for exceptions and missing routes
- Existing legacy API dispatch compatibility
- File-backed storage through `DatabaseManager`

## Run

```bash
dotnet run --urls http://localhost:5229
```

The default endpoints are:

- `GET /health`
- `GET /ready`
- `GET /api/v1/status`
- `GET /metrics`

The project targets .NET 10 and reads server settings from `appsettings.json`.
