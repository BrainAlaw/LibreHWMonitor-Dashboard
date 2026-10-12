using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Runtime.InteropServices;
using PCMonitor.Infra;

namespace PCMonitor.Ui;

internal sealed class DashboardForm : Form
{
    private readonly string dashboardUrl;
    private readonly Func<bool> shouldHideOnClose;
    private readonly WebView2 webView = new();
    private bool fullscreen;
    private Rectangle restoreBounds;
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
        Padding = new Padding(ResizeBorder);

        webView.Dock = DockStyle.Fill;
        webView.DefaultBackgroundColor = Color.FromArgb(15, 17, 21);
        Controls.Add(webView);

        FormClosing += OnFormClosing;
        KeyDown += OnKeyDown;
        Resize += (_, _) => OnWindowStateChanged();
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
            restoreWindowState = WindowState;
            restoreBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            fullscreen = true;
            WindowState = FormWindowState.Normal;
            Padding = Padding.Empty;
            Bounds = Screen.FromControl(this).Bounds;
            return;
        }

        fullscreen = false;
        WindowState = restoreWindowState;
        if (restoreWindowState == FormWindowState.Normal)
        {
            Bounds = restoreBounds;
        }

        OnWindowStateChanged();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // A 1px frame extension keeps the DWM drop shadow on the frameless window.
        var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        DwmExtendFrameIntoClientArea(Handle, ref margins);
    }

    /// <summary>
    /// Removes the standard title bar and border (the dashboard draws its own) while keeping the
    /// resizable window style, so snapping, the shadow and edge resizing keep working.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmNcCalcSize when m.WParam != IntPtr.Zero:
                if (WindowState == FormWindowState.Maximized && !fullscreen)
                {
                    var area = Screen.FromHandle(Handle).WorkingArea;
                    Marshal.WriteInt32(m.LParam, 0, area.Left);
                    Marshal.WriteInt32(m.LParam, 4, area.Top);
                    Marshal.WriteInt32(m.LParam, 8, area.Right);
                    Marshal.WriteInt32(m.LParam, 12, area.Bottom);
                }

                m.Result = IntPtr.Zero;
                return;

            case WmNcHitTest when !fullscreen && WindowState == FormWindowState.Normal:
                var hit = HitTestBorder(PointToClient(new Point(
                    (short)(m.LParam.ToInt64() & 0xFFFF),
                    (short)((m.LParam.ToInt64() >> 16) & 0xFFFF))));
                if (hit != 0)
                {
                    m.Result = hit;
                    return;
                }

                break;
        }

        base.WndProc(ref m);
    }

    private IntPtr HitTestBorder(Point p)
    {
        int corner = ResizeBorder * 2;
        bool left = p.X < ResizeBorder, right = p.X >= ClientSize.Width - ResizeBorder;
        bool top = p.Y < ResizeBorder, bottom = p.Y >= ClientSize.Height - ResizeBorder;
        bool nearLeft = p.X < corner, nearRight = p.X >= ClientSize.Width - corner;
        bool nearTop = p.Y < corner, nearBottom = p.Y >= ClientSize.Height - corner;

        if ((top && nearLeft) || (left && nearTop)) return HtTopLeft;
        if ((top && nearRight) || (right && nearTop)) return HtTopRight;
        if ((bottom && nearLeft) || (left && nearBottom)) return HtBottomLeft;
        if ((bottom && nearRight) || (right && nearBottom)) return HtBottomRight;
        if (left) return HtLeft;
        if (right) return HtRight;
        if (top) return HtTop;
        if (bottom) return HtBottom;
        return IntPtr.Zero;
    }

    private void OnWindowStateChanged()
    {
        bool maximized = WindowState == FormWindowState.Maximized;
        Padding = maximized || fullscreen ? Padding.Empty : new Padding(ResizeBorder);
        PostToDashboard(maximized ? "maximized" : "restored");
    }

    private void PostToDashboard(string message)
    {
        try
        {
            webView.CoreWebView2?.PostWebMessageAsString(message);
        }
        catch (InvalidOperationException)
        {
            // WebView is shutting down.
        }
    }

    /// <summary>Handles the custom title bar commands sent by the dashboard.</summary>
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        switch (args.TryGetWebMessageAsString())
        {
            case "drag":
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
                break;
            case "minimize":
                WindowState = FormWindowState.Minimized;
                break;
            case "maximize":
                if (!fullscreen)
                {
                    WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                }

                break;
            case "close":
                Close();
                break;
        }
    }

    private const int ResizeBorder = 6;
    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private static readonly IntPtr HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14,
        HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

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
            webView.CoreWebView2.WebMessageReceived += OnWebMessage;
            webView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    OnWindowStateChanged();
                }

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
