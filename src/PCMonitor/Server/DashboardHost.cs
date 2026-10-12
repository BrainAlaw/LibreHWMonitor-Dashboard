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

/// <summary>Loopback-only Kestrel host serving the embedded dashboard and the telemetry API.</summary>
internal sealed class DashboardHost : IAsyncDisposable
{
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
    public static async Task<DashboardHost> StartAsync(
        SettingsStore settings,
        TelemetryCollector collector,
        Func<bool, Task<bool>> setAutostart,
        Func<bool> isAutostartEnabled)
    {
        int requested = settings.ListenPort;
        Exception? lastError = null;

        for (int candidate = requested; candidate < Math.Min(requested + PortFallbackAttempts, 65536); candidate++)
        {
            WebApplication app = Build(candidate, settings, collector, setAutostart, isAutostartEnabled);
            try
            {
                await app.StartAsync();
                if (candidate != requested)
                {
                    Log.Info($"Port {requested} was busy, using {candidate}");
                }

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

    private static WebApplication Build(
        int port,
        SettingsStore settings,
        TelemetryCollector collector,
        Func<bool, Task<bool>> setAutostart,
        Func<bool> isAutostartEnabled)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, port);
        });

        WebApplication app = builder.Build();
        app.Use(LoopbackOnly(port));
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = CreateUiFileProvider(),
            OnPrepareResponse = static context =>
            {
                context.Context.Response.Headers.CacheControl = "no-store";
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
            }
        });

        Api.Map(app, port, settings, collector, setAutostart, isAutostartEnabled);
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = CreateUiFileProvider() });
        return app;
    }

    // Rejects DNS-rebinding style requests: only loopback host names with our port are served.
    private static Func<HttpContext, RequestDelegate, Task> LoopbackOnly(int port) => (context, next) =>
    {
        HostString host = context.Request.Host;
        bool allowed = host.Port == port &&
            (host.Host.Equals("127.0.0.1", StringComparison.Ordinal) ||
             host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
        if (allowed)
        {
            return next(context);
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };

    // A user-provided %APPDATA%\PCMonitor\ui folder wins over the embedded build, for UI development.
    private static IFileProvider CreateUiFileProvider()
    {
        ManifestEmbeddedFileProvider embedded = new(Assembly.GetExecutingAssembly(), "wwwroot");
        return Directory.Exists(AppPaths.UiOverrideDirectory)
            ? new CompositeFileProvider(new PhysicalFileProvider(AppPaths.UiOverrideDirectory), embedded)
            : embedded;
    }
}
