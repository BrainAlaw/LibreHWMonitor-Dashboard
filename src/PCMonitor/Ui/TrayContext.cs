using System.Diagnostics;
using PCMonitor.Infra;
using PCMonitor.Settings;
using PCMonitor.Telemetry;

namespace PCMonitor.Ui;

internal sealed class TrayContext : ApplicationContext
{
    private readonly TelemetryCollector collector;
    private readonly string dashboardUrl;
    private readonly NotifyIcon notifyIcon;
    private readonly DashboardForm form;
    private readonly System.Windows.Forms.Timer tooltipTimer = new() { Interval = 2000 };
    private readonly ToolStripMenuItem autostartItem = new("Start with Windows") { CheckOnClick = false };
    private bool exiting;

    public TrayContext(string dashboardUrl, TelemetryCollector collector, SettingsStore settings, bool startHidden)
    {
        this.dashboardUrl = dashboardUrl;
        this.collector = collector;

        Icon? icon = null;
        try
        {
            icon = Environment.ProcessPath is { } path ? Icon.ExtractAssociatedIcon(path) : null;
        }
        catch (Exception error)
        {
            Log.Error("Icon extraction failed", error);
        }

        form = new DashboardForm(dashboardUrl, icon, () => !exiting && settings.Current.CloseToTray);
        notifyIcon = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Text = "PC Monitor",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        notifyIcon.DoubleClick += (_, _) => ShowDashboard();

        tooltipTimer.Tick += (_, _) => UpdateTooltip();
        tooltipTimer.Start();

        // Creating the handle starts WebView2 loading even when the window stays hidden.
        _ = form.Handle;
        if (!startHidden)
        {
            ShowDashboard();
        }
    }

    public void ShowDashboard()
    {
        if (form.InvokeRequired)
        {
            form.BeginInvoke(ShowDashboard);
            return;
        }

        if (!form.Visible)
        {
            form.Show();
        }

        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }

        form.Activate();
    }

    private ContextMenuStrip BuildMenu()
    {
        ContextMenuStrip menu = new();
        menu.Opening += (_, _) => autostartItem.Checked = Autostart.IsEnabled();
        autostartItem.Click += (_, _) => ToggleAutostart();

        menu.Items.Add("Open", null, (_, _) => ShowDashboard());
        menu.Items.Add("Fullscreen", null, (_, _) => { ShowDashboard(); form.ToggleFullscreen(); });
        menu.Items.Add("Reload", null, (_, _) => form.ReloadDashboard());
        menu.Items.Add("Reset telemetry", null, (_, _) => ResetTelemetry());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(autostartItem);
        menu.Items.Add("Open in browser", null, (_, _) => OpenShell(dashboardUrl));
        menu.Items.Add("Open logs folder", null, (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            OpenShell(AppPaths.LogDirectory);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        return menu;
    }

    private void UpdateTooltip()
    {
        notifyIcon.Text = TraySummary.Build(collector.Latest?.Snapshot);
    }

    private void ResetTelemetry()
    {
        notifyIcon.ShowBalloonTip(1000, "PC Monitor", "Resetting telemetry collector...", ToolTipIcon.Info);
        collector.Reset();
        form.ReloadDashboard();
    }

    private void ToggleAutostart()
    {
        bool enable = !Autostart.IsEnabled();
        string exePath = Environment.ProcessPath ?? string.Empty;
        bool ok = enable ? Autostart.Enable(exePath) : Autostart.Disable();
        if (!ok)
        {
            notifyIcon.ShowBalloonTip(2000, "PC Monitor", "Could not change the autostart task.", ToolTipIcon.Error);
        }
    }

    private static void OpenShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            Log.Error($"Could not open {target}", error);
        }
    }

    private void ExitApplication()
    {
        exiting = true;
        tooltipTimer.Stop();
        notifyIcon.Visible = false;
        form.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tooltipTimer.Dispose();
            notifyIcon.Dispose();
            form.Dispose();
        }

        base.Dispose(disposing);
    }
}
