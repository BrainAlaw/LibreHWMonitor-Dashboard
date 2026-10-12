using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using PCMonitor.Infra;
using PCMonitor.Settings;
using PCMonitor.Telemetry;

namespace PCMonitor.Server;

/// <summary>OS-level side effects the API can trigger, injected so the host stays testable.</summary>
internal sealed record SystemActions(
    Func<bool, Task<bool>> SetAutostart,
    Func<bool> IsAutostartEnabled,
    Func<bool, Task<bool>> SetLanFirewall);

/// <summary>Kestrel host serving the embedded dashboard and the telemetry API (loopback, optionally LAN).</summary>
internal sealed class DashboardHost : IAsyncDisposable
{
    internal const string TokenCookie = "pcmonitor_token";
    private const int PortFallbackAttempts = 20;

    private readonly WebApplication app;

    private DashboardHost(WebApplication app, int port)
    {
        this.app = app;
        Port = port;
    }

    public int Port { get; }
    public string Url => $"http://127.0.0.1:{Port}/";

    /// <summary>Starts on the requested port, or the next free one when it is taken.</summary>
    public static async Task<DashboardHost> StartAsync(SettingsStore settings, TelemetryCollector collector, SystemActions actions)
    {
        int requested = settings.ListenPort;
        Exception? lastError = null;

        for (int candidate = requested; candidate < Math.Min(requested + PortFallbackAttempts, 65536); candidate++)
        {
            WebApplication app = Build(candidate, settings, collector, actions);
            try
            {
                await app.StartAsync();
                if (candidate != requested)
                {
                    Log.Info($"Port {requested} was busy, using {candidate}");
                }

                Log.Info($"Dashboard listening on port {candidate}, LAN access {(settings.Current.AllowLan ? "ON" : "off")}");
                return new DashboardHost(app, candidate);
            }
            catch (IOException error)
            {
                // Kestrel reports an address that is already in use as an IOException.
                lastError = error;
                await app.DisposeAsync();
            }
        }

        throw new InvalidOperationException($"No free port found starting at {requested}: {lastError?.Message}", lastError);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            await app.StopAsync(timeout.Token);
        }
        catch (Exception error)
        {
            Log.Error("Server shutdown failed", error);
        }

        await app.DisposeAsync();
    }

    private static WebApplication Build(int port, SettingsStore settings, TelemetryCollector collector, SystemActions actions)
    {
        // The bind address is fixed for the lifetime of the host; changing AllowLan needs a restart.
        IPAddress bindAddress = settings.Current.AllowLan ? IPAddress.Any : IPAddress.Loopback;

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(bindAddress, port);
        });

        WebApplication app = builder.Build();
        app.Use(Authorize(port, settings));
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = CreateUiFileProvider(),
            OnPrepareResponse = static context =>
            {
                context.Context.Response.Headers.CacheControl = "no-store";
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
            }
        });

        Api.Map(app, port, settings, collector, actions);
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = CreateUiFileProvider() });
        return app;
    }

    private static Func<HttpContext, RequestDelegate, Task> Authorize(int port, SettingsStore settings) => async (context, next) =>
    {
        AppSettings current = settings.Current;
        HttpRequest request = context.Request;
        string? queryToken = request.Query["token"];
        string? presented = queryToken ?? request.Cookies[TokenCookie] ?? BearerToken(request);

        AccessDecision decision = AccessPolicy.Evaluate(
            request.Host.Host,
            request.Host.Port,
            context.Connection.RemoteIpAddress,
            port,
            current.AllowLan,
            Environment.MachineName,
            current.AccessToken,
            presented);

        switch (decision)
        {
            case AccessDecision.ForbiddenHost:
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            case AccessDecision.Unauthorized:
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("Access token required. Open the full link shown in PC Monitor (Settings > LAN access).");
                return;
        }

        // A valid token in the URL becomes a cookie, then the token is dropped from the address bar.
        if (queryToken is not null && current.AccessToken is not null && AccessPolicy.TokensMatch(current.AccessToken, queryToken))
        {
            context.Response.Cookies.Append(TokenCookie, queryToken, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                MaxAge = TimeSpan.FromDays(365)
            });

            if (HttpMethods.IsGet(request.Method))
            {
                context.Response.Redirect(request.Path.HasValue ? request.Path.Value! : "/");
                return;
            }
        }

        await next(context);
    };

    private static string? BearerToken(HttpRequest request)
    {
        string? header = request.Headers.Authorization;
        return header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }

    // A user-provided %APPDATA%\PCMonitor\ui folder wins over the embedded build, for UI development.
    private static IFileProvider CreateUiFileProvider()
    {
        ManifestEmbeddedFileProvider embedded = new(Assembly.GetExecutingAssembly(), "wwwroot");
        return Directory.Exists(AppPaths.UiOverrideDirectory)
            ? new CompositeFileProvider(new PhysicalFileProvider(AppPaths.UiOverrideDirectory), embedded)
            : embedded;
    }
}
