using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using OpenRec_2.API;

namespace OpenRec_2;

public static class Server
{
    private static readonly IReadOnlyList<IApi> ApiList =
    [
        new DefaultApi(),
        new TestApi(),
        new CharadesWordsApi(),
        new AmplitudeApi(),
        new ConfigV2Api(),
        new VersionCheckApi()
    ];

    public static async Task StartAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Server"));
        builder.Services.AddSingleton<ServerMetrics>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<RoomService>();
        builder.WebHost.ConfigureKestrel(options =>
        {
            var limit = builder.Configuration.GetValue<int?>("Server:MaxRequestBodySize") ?? 10485760;
            options.Limits.MaxRequestBodySize = limit;
        });

        var app = builder.Build();
        var options = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;
        var metrics = app.Services.GetRequiredService<ServerMetrics>();
        var auth = app.Services.GetRequiredService<AuthService>();
        var rooms = app.Services.GetRequiredService<RoomService>();

        if (options.EnableCors)
        {
            app.Use(async (context, next) =>
            {
                context.Response.Headers.AccessControlAllowOrigin = "*";
                context.Response.Headers.AccessControlAllowMethods = "GET,POST,PUT,DELETE,OPTIONS";
                context.Response.Headers.AccessControlAllowHeaders = "Content-Type,Authorization,X-Rec-Room-Version,X-Request-Id";
                if (HttpMethods.IsOptions(context.Request.Method))
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }
                await next();
            });
        }

        app.Use(async (context, next) =>
        {
            var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(requestId))
            {
                requestId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
            }
            context.Response.Headers["X-Request-Id"] = requestId;
            using var scope = metrics.BeginRequest();
            try
            {
                await next();
            }
            catch (Exception exception)
            {
                metrics.RecordError();
                app.Logger.LogError(exception, "Request {RequestId} failed", requestId);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new { error = "internal_server_error", requestId });
                }
            }
        });

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = options.Name,
            version = options.Version,
            timestamp = DateTimeOffset.UtcNow
        }));

        app.MapGet("/ready", () => Results.Ok(new
        {
            status = "ready",
            database = "file-storage",
            timestamp = DateTimeOffset.UtcNow
        }));

        app.MapGet("/api/v1/status", () => Results.Ok(new
        {
            service = options.Name,
            version = options.Version,
            supportedRecRoomVersion = options.SupportedRecRoomVersion,
            region = options.CloudRegion,
            users = auth.UserCount,
            sessions = auth.SessionCount,
            rooms = rooms.Count,
            uptimeSeconds = (long)(DateTimeOffset.UtcNow - metrics.StartedAt).TotalSeconds,
            requests = metrics.Requests,
            errors = metrics.Errors,
            activeRequests = metrics.ActiveRequests
        }));

        app.MapGet("/metrics", () => Results.Text(
            $"openrec_requests_total {metrics.Requests}\n" +
            $"openrec_errors_total {metrics.Errors}\n" +
            $"openrec_active_requests {metrics.ActiveRequests}\n" +
            $"openrec_users_total {auth.UserCount}\n" +
            $"openrec_rooms_total {rooms.Count}\n" +
            $"openrec_uptime_seconds {(long)(DateTimeOffset.UtcNow - metrics.StartedAt).TotalSeconds}\n",
            "text/plain"));

        app.MapPost("/api/v1/auth/register", async (HttpContext context) =>
        {
            var request = await context.Request.ReadFromJsonAsync<RegisterRequest>();
            if (request is null)
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }
            var user = auth.Register(request.Username, request.Password);
            return user is null
                ? Results.Conflict(new { error = "username_unavailable_or_invalid" })
                : Results.Created($"/api/v1/auth/users/{user.Id}", user);
        });

        app.MapPost("/api/v1/auth/login", async (HttpContext context) =>
        {
            var request = await context.Request.ReadFromJsonAsync<LoginRequest>();
            if (request is null)
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }
            var result = auth.Login(request.Username, request.Password);
            return result is null
                ? Results.Unauthorized()
                : Results.Ok(new { user = result.User, token = result.Token, expiresAt = result.ExpiresAt });
        });

        app.MapPost("/api/v1/auth/logout", (HttpContext context) =>
            auth.Logout(context) ? Results.NoContent() : Results.Unauthorized());

        app.MapGet("/api/v1/auth/me", (HttpContext context) =>
        {
            var user = auth.Authenticate(context);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        });

        app.MapGet("/api/v1/rooms", (HttpContext context) =>
        {
            var region = context.Request.Query["region"].FirstOrDefault();
            var activity = context.Request.Query["activity"].FirstOrDefault();
            return Results.Ok(new { rooms = rooms.List(region, activity) });
        });

        app.MapPost("/api/v1/rooms", async (HttpContext context) =>
        {
            var user = auth.Authenticate(context);
            if (user is null)
            {
                return Results.Unauthorized();
            }
            var request = await context.Request.ReadFromJsonAsync<CreateRoomRequest>();
            if (request is null)
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }
            var room = rooms.Create(user, request.Name, request.Activity ?? "Hangout", request.Region ?? options.CloudRegion, request.Capacity, request.IsPrivate);
            return room is null ? Results.BadRequest(new { error = "invalid_room_configuration" }) : Results.Created($"/api/v1/rooms/{room.Id}", room);
        });

        app.MapGet("/api/v1/rooms/{id:guid}", (Guid id) =>
        {
            var room = rooms.Get(id);
            return room is null ? Results.NotFound(new { error = "room_not_found" }) : Results.Ok(room);
        });

        app.MapPost("/api/v1/rooms/{id:guid}/join", (Guid id, HttpContext context) =>
        {
            var user = auth.Authenticate(context);
            if (user is null)
            {
                return Results.Unauthorized();
            }
            if (rooms.Get(id) is null)
            {
                return Results.NotFound(new { error = "room_not_found" });
            }
            return rooms.Join(id, user) ? Results.Ok(rooms.Get(id)) : Results.Conflict(new { error = "room_full" });
        });

        app.MapPost("/api/v1/rooms/{id:guid}/leave", (Guid id, HttpContext context) =>
        {
            var user = auth.Authenticate(context);
            if (user is null)
            {
                return Results.Unauthorized();
            }
            if (rooms.Get(id) is null)
            {
                return Results.NotFound(new { error = "room_not_found" });
            }
            return rooms.Leave(id, user) ? Results.NoContent() : Results.BadRequest(new { error = "owner_cannot_leave_or_user_not_present" });
        });

        app.MapDelete("/api/v1/rooms/{id:guid}", (Guid id, HttpContext context) =>
        {
            var user = auth.Authenticate(context);
            if (user is null)
            {
                return Results.Unauthorized();
            }
            if (rooms.Get(id) is null)
            {
                return Results.NotFound(new { error = "room_not_found" });
            }
            return rooms.Delete(id, user) ? Results.NoContent() : Results.Forbid();
        });

        app.Run(async context =>
        {
            var path = context.Request.Path.Value ?? "/";
            var method = context.Request.Method;
            var api = ApiList.FirstOrDefault(candidate => path == candidate.GetUrl() && method == candidate.GetMethod());
            if (api is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "endpoint_not_found", path, method });
                return;
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(api.Response(context));
        });

        await app.RunAsync();
    }
}
