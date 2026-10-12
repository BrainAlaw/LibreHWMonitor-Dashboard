using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using PCMonitor.Infra;
using PCMonitor.Server;
using PCMonitor.Settings;
using PCMonitor.Telemetry;
using PCMonitor.Ui;

namespace PCMonitor;

internal static class Program
{
    private const string InstanceMutexName = @"Local\PCMonitor.Dashboard.Instance";
    private const string ShowEventName = @"Local\PCMonitor.Dashboard.Show";
    private const string WebViewDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        using Mutex instanceMutex = new(true, InstanceMutexName, out bool isFirstInstance);
        using EventWaitHandle showEvent = new(false, EventResetMode.AutoReset, ShowEventName);
        if (!isFirstInstance)
        {
            showEvent.Set();
            return 0;
        }

        Log.Prune();
        bool elevated = Elevation.IsElevated();
        Log.Info($"Starting PC Monitor, elevated={elevated}, args=[{string.Join(' ', args)}]");

        try
        {
            return Run(args, showEvent, elevated);
        }
        catch (Exception error)
        {
            Log.Error("Fatal error", error);
            MessageBox.Show($"PC Monitor crashed.\n\n{error.Message}\n\nLogs: {AppPaths.LogDirectory}",
                "PC Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Run(string[] args, EventWaitHandle showEvent, bool elevated)
    {
        SettingsStore settings = new();
        bool startHidden = settings.Current.StartMinimized ||
            args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        using TelemetryCollector collector = new(settings, static s => new LhmHardwareSource(s), elevated);

        DashboardHost host;
        try
        {
            host = DashboardHost.StartAsync(settings, collector, CreateSystemActions()).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Log.Error("Dashboard server did not start", error);
            MessageBox.Show($"Dashboard server did not start.\n\n{error.Message}",
                "PC Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        try
        {
            collector.Start();

            if (!IsWebViewAvailable())
            {
                return RunBrowserFallback(host.Url, showEvent);
            }

            using TrayContext context = new(host.Url, collector, settings, startHidden);
            RegisterShowSignal(showEvent, context.ShowDashboard);
            Application.Run(context);
            return 0;
        }
        finally
        {
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static SystemActions CreateSystemActions()
    {
        static string ExePath() => Environment.ProcessPath ?? string.Empty;
        return new SystemActions(
            static enable => Task.Run(() => enable ? Autostart.Enable(ExePath()) : Autostart.Disable()),
            Autostart.IsEnabled,
            static enable => Task.Run(() => Firewall.Apply(enable, ExePath())));
    }

    private static bool IsWebViewAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception error)
        {
            Log.Error("WebView2 runtime check failed", error);
            return false;
        }
    }

    // Without the WebView2 runtime the dashboard still works in the default browser.
    private static int RunBrowserFallback(string url, EventWaitHandle showEvent)
    {
        DialogResult answer = MessageBox.Show(
            "The Microsoft WebView2 Runtime is not installed, so the native window is unavailable.\n\n" +
            "Yes: open the dashboard in your default browser (the app keeps running in the tray).\n" +
            $"No: exit and open the WebView2 download page ({WebViewDownloadUrl}).",
            "PC Monitor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (answer == DialogResult.No)
        {
            Process.Start(new ProcessStartInfo(WebViewDownloadUrl) { UseShellExecute = true });
            return 0;
        }

        void OpenBrowser() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        OpenBrowser();

        using NotifyIcon tray = new()
        {
            Icon = SystemIcons.Application,
            Text = "PC Monitor (browser mode)",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        tray.ContextMenuStrip.Items.Add("Open in browser", null, (_, _) => OpenBrowser());
        tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Application.ExitThread());
        tray.DoubleClick += (_, _) => OpenBrowser();
        RegisterShowSignal(showEvent, OpenBrowser);
        Application.Run();
        return 0;
    }

    // A second launch signals the first instance, which brings its window forward.
    private static void RegisterShowSignal(EventWaitHandle showEvent, Action show)
    {
        ThreadPool.RegisterWaitForSingleObject(
            showEvent,
            (_, _) => show(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }
}
