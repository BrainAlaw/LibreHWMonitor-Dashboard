using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PCMonitor.Infra;

namespace PCMonitor.Ui;

internal sealed class DashboardForm : Form
{
    private readonly string dashboardUrl;
    private readonly Func<bool> shouldHideOnClose;
    private readonly WebView2 webView = new();
    private bool fullscreen;
    private Rectangle restoreBounds;
    private FormBorderStyle restoreBorderStyle;
    private FormWindowState restoreWindowState;

    public DashboardForm(string dashboardUrl, Icon? icon, Func<bool> shouldHideOnClose)
    {
        this.dashboardUrl = dashboardUrl;
        this.shouldHideOnClose = shouldHideOnClose;

        Text = "PC Monitor";
        Icon = icon;
        Width = 1600;
        Height = 950;
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BackColor = Color.FromArgb(15, 17, 21);

        webView.Dock = DockStyle.Fill;
        webView.DefaultBackgroundColor = Color.FromArgb(15, 17, 21);
        Controls.Add(webView);

        FormClosing += OnFormClosing;
        KeyDown += OnKeyDown;
        Load += async (_, _) => await InitializeWebViewAsync();
    }

    public void ReloadDashboard()
    {
        webView.CoreWebView2?.Reload();
    }

    public void ToggleFullscreen()
    {
        if (!fullscreen)
        {
            restoreBounds = Bounds;
            restoreBorderStyle = FormBorderStyle;
            restoreWindowState = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
            fullscreen = true;
            return;
        }

        FormBorderStyle = restoreBorderStyle;
        WindowState = restoreWindowState;
        Bounds = restoreBounds;
        fullscreen = false;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.WebViewDirectory);
            webView.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = AppPaths.WebViewDirectory
            };

            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    Log.Error($"Navigation failed: {args.WebErrorStatus}");
                }
            };
            webView.CoreWebView2.Navigate(dashboardUrl);
        }
        catch (Exception error)
        {
            Log.Error("WebView2 initialization failed", error);
            MessageBox.Show(
                $"WebView2 could not start.\n\n{error.Message}",
                "PC Monitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (!shouldHideOnClose())
        {
            return;
        }

        args.Cancel = true;
        Hide();
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyCode == Keys.F11 || (args.KeyCode == Keys.Escape && fullscreen))
        {
            ToggleFullscreen();
        }
    }
}
