using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PCMonitor.Infra;
using PCMonitor.Settings;
using PCMonitor.Telemetry;

namespace PCMonitor.Server;

/// <summary>
/// Loopback-only HTTP server: embedded dashboard, /data.json, /api/health, /api/settings.
/// </summary>
internal sealed class DashboardServer : IDisposable
{
    private const int PortFallbackAttempts = 20;
    private const int MaxSettingsBodyBytes = 4096;

    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web);

    private readonly SettingsStore settings;
    private readonly TelemetryCollector collector;
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(4) };
    private HttpListener? listener;
    private CancellationTokenSource? cancellation;
    private Task? serverTask;

    public DashboardServer(SettingsStore settings, TelemetryCollector collector)
    {
        this.settings = settings;
        this.collector = collector;
    }

    public int Port { get; private set; }
    public string Url => $"http://127.0.0.1:{Port}/";

    /// <summary>Binds the configured port, or the next free one when it is taken.</summary>
    public void Start()
    {
        int requested = settings.Current.Port;
        Exception? lastError = null;

        for (int offset = 0; offset < PortFallbackAttempts; offset++)
        {
            int candidate = requested + offset;
            if (candidate > 65535)
            {
                break;
            }

            HttpListener attempt = new();
            attempt.Prefixes.Add($"http://127.0.0.1:{candidate}/");
            try
            {
                attempt.Start();
                listener = attempt;
                Port = candidate;
                if (offset > 0)
                {
                    Log.Info($"Port {requested} was busy, using {candidate}");
                }

                cancellation = new CancellationTokenSource();
                serverTask = Task.Run(() => RunAsync(cancellation.Token));
                return;
            }
            catch (HttpListenerException error)
            {
                lastError = error;
                attempt.Close();
            }
        }

        throw new InvalidOperationException(
            $"No free port found starting at {requested}: {lastError?.Message}", lastError);
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        try
        {
            listener?.Close();
        }
        catch
        {
            // Listener may already be closed.
        }

        httpClient.Dispose();
        try
        {
            serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown is best effort; process exit reclaims the listener.
        }

        cancellation?.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener is { IsListening: true })
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (HttpListenerException)
            {
                if (listener is not { IsListening: true })
                {
                    return;
                }

                continue;
            }

            _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            if (!IsAllowedHost(context.Request))
            {
                await WriteTextAsync(context.Response, 403, "text/plain; charset=utf-8", "Forbidden");
                return;
            }

            string path = context.Request.Url?.AbsolutePath ?? "/";
            string method = context.Request.HttpMethod;

            switch (path.ToLowerInvariant())
            {
                case "/" or "/index.html" when method == "GET":
                    await ServeIndexAsync(context.Response);
                    break;
                case "/data.json" when method == "GET":
                    await ServeTelemetryAsync(context.Response);
                    break;
                case "/api/health" when method == "GET":
                    await WriteJsonAsync(context.Response, 200, BuildHealth());
                    break;
                case "/api/settings" when method == "GET":
                    await WriteJsonAsync(context.Response, 200, BuildSettingsView());
                    break;
                case "/api/settings" when method == "PUT":
                    await UpdateSettingsAsync(context);
                    break;
                case "/favicon.ico":
                    await WriteTextAsync(context.Response, 204, "image/x-icon", string.Empty);
                    break;
                default:
                    await WriteTextAsync(context.Response, 404, "text/plain; charset=utf-8", "Not found");
                    break;
            }
        }
        catch (Exception error)
        {
            Log.Error($"Request failed: {context.Request.Url}", error);
            try
            {
                await WriteJsonAsync(context.Response, 500, new { error = "Internal server error" });
            }
            catch
            {
                // Response is already broken; nothing left to do.
            }
        }
    }

    // Rejects DNS-rebinding style requests: only loopback host names with our port are served.
    private bool IsAllowedHost(HttpListenerRequest request)
    {
        string? host = request.Headers["Host"];
        return host is not null &&
            (host.Equals($"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase) ||
             host.Equals($"localhost:{Port}", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSameOrigin(HttpListenerRequest request)
    {
        string? origin = request.Headers["Origin"];
        return origin is not null &&
            (origin.Equals($"http://127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase) ||
             origin.Equals($"http://localhost:{Port}", StringComparison.OrdinalIgnoreCase));
    }

    private async Task ServeIndexAsync(HttpListenerResponse response)
    {
        string overridePath = Path.Combine(AppPaths.UiOverrideDirectory, "index.html");
        byte[] bytes;
        if (File.Exists(overridePath))
        {
            bytes = await File.ReadAllBytesAsync(overridePath);
        }
        else
        {
            await using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("index.html")
                ?? throw new FileNotFoundException("Embedded index.html is missing");
            using MemoryStream buffer = new();
            await stream.CopyToAsync(buffer);
            bytes = buffer.ToArray();
        }

        await WriteBytesAsync(response, 200, "text/html; charset=utf-8", bytes);
    }

    private async Task ServeTelemetryAsync(HttpListenerResponse response)
    {
        string? externalSource = settings.Current.ExternalSource;
        if (externalSource is null)
        {
            await WriteTextAsync(response, 200, "application/json; charset=utf-8", collector.SnapshotJson,
                new Dictionary<string, string> { ["X-PCMonitor-Telemetry"] = "embedded" });
            return;
        }

        try
        {
            using HttpResponseMessage upstream = await httpClient.GetAsync(externalSource);
            string body = await upstream.Content.ReadAsStringAsync();
            if (!upstream.IsSuccessStatusCode || !TelemetryJson.LooksLikeLibreHardwarePayload(body))
            {
                throw new InvalidOperationException($"Invalid external telemetry response: {(int)upstream.StatusCode}");
            }

            await WriteTextAsync(response, 200, "application/json; charset=utf-8", body,
                new Dictionary<string, string>
                {
                    ["X-PCMonitor-Telemetry"] = "external",
                    ["X-PCMonitor-Source"] = externalSource
                });
        }
        catch (Exception error)
        {
            await WriteJsonAsync(response, 502, new
            {
                error = "Failed to fetch external telemetry",
                detail = error.Message,
                source = externalSource
            });
        }
    }

    private object BuildHealth()
    {
        TelemetryHealth health = collector.Health;
        bool external = settings.Current.ExternalSource is not null;
        return new
        {
            status = external ? "ok" : health.Status,
            mode = external ? "external" : health.Mode,
            elevated = health.Elevated,
            hardwareCount = health.HardwareCount,
            sensorCount = health.SensorCount,
            lastError = external ? null : health.LastError,
            lastUpdate = health.LastUpdate,
            port = Port,
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        };
    }

    private object BuildSettingsView()
    {
        AppSettings current = settings.Current;
        return new
        {
            port = current.Port,
            samplingMs = current.SamplingMs,
            startMinimized = current.StartMinimized,
            closeToTray = current.CloseToTray,
            autostart = Autostart.IsEnabled()
        };
    }

    private async Task UpdateSettingsAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        if (!IsSameOrigin(request) ||
            request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            await WriteJsonAsync(context.Response, 403, new { error = "Cross-origin or non-JSON request rejected" });
            return;
        }

        if (request.ContentLength64 is < 0 or > MaxSettingsBodyBytes)
        {
            await WriteJsonAsync(context.Response, 413, new { error = "Request body too large" });
            return;
        }

        SettingsPatch? patch;
        try
        {
            patch = await JsonSerializer.DeserializeAsync<SettingsPatch>(request.InputStream, ApiJson);
        }
        catch (JsonException)
        {
            patch = null;
        }

        if (patch is null)
        {
            await WriteJsonAsync(context.Response, 400, new { error = "Invalid JSON" });
            return;
        }

        if (patch.Port is < 1 or > 65535 || patch.SamplingMs is < 250 or > 60_000)
        {
            await WriteJsonAsync(context.Response, 400, new { error = "Port or sampling interval out of range" });
            return;
        }

        bool restartRequired = patch.Port.HasValue && patch.Port.Value != settings.Current.Port;
        settings.Update(s =>
        {
            s.Port = patch.Port ?? s.Port;
            s.SamplingMs = patch.SamplingMs ?? s.SamplingMs;
            s.StartMinimized = patch.StartMinimized ?? s.StartMinimized;
            s.CloseToTray = patch.CloseToTray ?? s.CloseToTray;
        });

        if (patch.Autostart is bool autostart)
        {
            string exePath = Environment.ProcessPath ?? string.Empty;
            bool applied = autostart ? Autostart.Enable(exePath) : Autostart.Disable();
            if (!applied)
            {
                await WriteJsonAsync(context.Response, 500, new { error = "Autostart task could not be changed" });
                return;
            }
        }

        await WriteJsonAsync(context.Response, 200, new { ok = true, restartRequired });
    }

    private sealed record SettingsPatch(
        int? Port,
        int? SamplingMs,
        bool? StartMinimized,
        bool? CloseToTray,
        bool? Autostart);

    private static Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload) =>
        WriteTextAsync(response, statusCode, "application/json; charset=utf-8",
            JsonSerializer.Serialize(payload, ApiJson));

    private static Task WriteTextAsync(
        HttpListenerResponse response,
        int statusCode,
        string contentType,
        string body,
        IReadOnlyDictionary<string, string>? headers = null) =>
        WriteBytesAsync(response, statusCode, contentType, Encoding.UTF8.GetBytes(body), headers);

    private static async Task WriteBytesAsync(
        HttpListenerResponse response,
        int statusCode,
        string contentType,
        byte[] bytes,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        if (headers is not null)
        {
            foreach ((string key, string value) in headers)
            {
                response.Headers[key] = value;
            }
        }

        response.ContentLength64 = bytes.Length;
        if (bytes.Length > 0)
        {
            await response.OutputStream.WriteAsync(bytes);
        }

        response.Close();
    }
}
