using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using PCMonitor.Settings;
using PCMonitor.Telemetry;

namespace PCMonitor.Server;

internal static class Api
{
    private const int MaxSettingsBodyBytes = 4096;
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Map(
        IEndpointRouteBuilder routes,
        int port,
        SettingsStore settings,
        TelemetryCollector collector,
        Func<bool, Task<bool>> setAutostart,
        Func<bool> isAutostartEnabled)
    {
        routes.MapGet("/api/snapshot", (HttpContext context) =>
        {
            PublishedSnapshot? latest = collector.Latest;
            context.Response.Headers.CacheControl = "no-store";
            return latest is null
                ? Results.Json(new { error = "Telemetry is still starting" }, Json, statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Text(latest.Json, "application/json; charset=utf-8");
        });

        routes.MapGet("/api/health", () => Results.Json(BuildHealth(collector, port), Json));

        routes.MapGet("/api/stream", (HttpContext context) => StreamAsync(context, collector, port));

        routes.MapGet("/api/settings", () => Results.Json(new
        {
            port = settings.Current.Port,
            samplingMs = settings.Current.SamplingMs,
            startMinimized = settings.Current.StartMinimized,
            closeToTray = settings.Current.CloseToTray,
            autostart = isAutostartEnabled()
        }, Json));

        routes.MapPut("/api/settings", async (HttpContext context) =>
        {
            if (!IsSameOrigin(context.Request, port) ||
                context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
            {
                return Results.Json(new { error = "Cross-origin or non-JSON request rejected" }, Json, statusCode: StatusCodes.Status403Forbidden);
            }

            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = MaxSettingsBodyBytes;
            }

            SettingsPatch? patch;
            try
            {
                patch = await context.Request.ReadFromJsonAsync<SettingsPatch>(Json, context.RequestAborted);
            }
            catch (BadHttpRequestException)
            {
                return Results.Json(new { error = "Request body too large" }, Json, statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            catch (JsonException)
            {
                patch = null;
            }

            if (patch is null)
            {
                return Results.Json(new { error = "Invalid JSON" }, Json, statusCode: StatusCodes.Status400BadRequest);
            }

            if ((patch.Port is int p && !AppSettings.IsValidPort(p)) ||
                (patch.SamplingMs is int ms && !AppSettings.IsValidSampling(ms)))
            {
                return Results.Json(new { error = "Port or sampling interval out of range" }, Json, statusCode: StatusCodes.Status400BadRequest);
            }

            bool restartRequired = patch.Port.HasValue && patch.Port.Value != settings.ListenPort;
            settings.Update(s =>
            {
                s.Port = patch.Port ?? s.Port;
                s.SamplingMs = patch.SamplingMs ?? s.SamplingMs;
                s.StartMinimized = patch.StartMinimized ?? s.StartMinimized;
                s.CloseToTray = patch.CloseToTray ?? s.CloseToTray;
            });

            if (patch.Autostart is bool autostart && !await setAutostart(autostart))
            {
                return Results.Json(new { error = "Autostart task could not be changed" }, Json, statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Json(new { ok = true, restartRequired }, Json);
        });
    }

    private static async Task StreamAsync(HttpContext context, TelemetryCollector collector, int port)
    {
        HttpResponse response = context.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Accel-Buffering"] = "no";

        CancellationToken token = context.RequestAborted;
        long seen = 0;
        string? lastHealth = null;

        try
        {
            while (!token.IsCancellationRequested)
            {
                bool wrote = false;

                PublishedSnapshot? latest = collector.Latest;
                if (latest is not null && latest.Version > seen)
                {
                    seen = latest.Version;
                    await WriteEventAsync(response, "snapshot", latest.Json, token);
                    wrote = true;
                }

                string health = JsonSerializer.Serialize(BuildHealth(collector, port), Json);
                if (health != lastHealth)
                {
                    lastHealth = health;
                    await WriteEventAsync(response, "health", health, token);
                    wrote = true;
                }

                if (!wrote)
                {
                    await response.WriteAsync(": ping\n\n", token);
                    await response.Body.FlushAsync(token);
                }

                await collector.WaitForChangeAsync(seen, Heartbeat, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected.
        }
    }

    private static async Task WriteEventAsync(HttpResponse response, string name, string json, CancellationToken token)
    {
        // Snapshot JSON is compact (no newlines), so a single data line is valid SSE.
        await response.WriteAsync($"event: {name}\ndata: {json}\n\n", Encoding.UTF8, token);
        await response.Body.FlushAsync(token);
    }

    private static object BuildHealth(TelemetryCollector collector, int port)
    {
        TelemetryHealth health = collector.Health;
        return new
        {
            status = health.Status,
            elevated = health.Elevated,
            hardwareCount = health.HardwareCount,
            sensorCount = health.SensorCount,
            lastError = health.LastError,
            lastUpdate = health.LastUpdate,
            port,
            version = typeof(Api).Assembly.GetName().Version?.ToString(3)
        };
    }

    private static bool IsSameOrigin(HttpRequest request, int port)
    {
        string? origin = request.Headers.Origin;
        return origin is not null &&
            (origin.Equals($"http://127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase) ||
             origin.Equals($"http://localhost:{port}", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record SettingsPatch(
        int? Port,
        int? SamplingMs,
        bool? StartMinimized,
        bool? CloseToTray,
        bool? Autostart);
}
