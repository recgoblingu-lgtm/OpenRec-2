using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
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
        builder.WebHost.ConfigureKestrel(options =>
        {
            var limit = builder.Configuration.GetValue<int?>("Server:MaxRequestBodySize") ?? 10485760;
            options.Limits.MaxRequestBodySize = limit;
        });

        var app = builder.Build();
        var options = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;
        var metrics = app.Services.GetRequiredService<ServerMetrics>();

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
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "internal_server_error",
                        requestId
                    });
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
            uptimeSeconds = (long)(DateTimeOffset.UtcNow - metrics.StartedAt).TotalSeconds,
            requests = metrics.Requests,
            errors = metrics.Errors,
            activeRequests = metrics.ActiveRequests
        }));

        app.MapGet("/metrics", () => Results.Text(
            $"openrec_requests_total {metrics.Requests}\n" +
            $"openrec_errors_total {metrics.Errors}\n" +
            $"openrec_active_requests {metrics.ActiveRequests}\n" +
            $"openrec_uptime_seconds {(long)(DateTimeOffset.UtcNow - metrics.StartedAt).TotalSeconds}\n",
            "text/plain"));

        app.Run(async context =>
        {
            var path = context.Request.Path.Value ?? "/";
            var method = context.Request.Method;
            var api = ApiList.FirstOrDefault(candidate => path == candidate.GetUrl() && method == candidate.GetMethod());
            if (api is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "endpoint_not_found",
                    path,
                    method
                });
                return;
            }

            context.Response.ContentType = "application/json";
            var response = api.Response(context);
            await context.Response.WriteAsync(response);
        });

        await app.RunAsync();
    }
}
