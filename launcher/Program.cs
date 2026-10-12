using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        const int defaultDashboardPort = 8096;

        int dashboardPort = ReadPort("PORT", defaultDashboardPort);
        string dashboardDirectory = ResolveDashboardDirectory();
        string dashboardUrl = $"http://127.0.0.1:{dashboardPort}/";
        string? externalTelemetrySource = ReadExternalTelemetrySource();

        if (!File.Exists(Path.Combine(dashboardDirectory, "index.html")))
        {
            ShowError($"index.html was not found in:\n{dashboardDirectory}");
            return 1;
        }

        using EmbeddedTelemetryCollector collector = new();
        using DashboardServer server = new(dashboardDirectory, dashboardPort, collector, externalTelemetrySource);

        try
        {
            server.Start();
        }
        catch (Exception error)
        {
            ShowError($"Dashboard server did not start on port {dashboardPort}.\n\n{error.Message}");
            return 1;
        }

        ApplicationConfiguration.Initialize();
        using DashboardAppContext context = new(
            dashboardUrl,
            () =>
            {
                collector.Reset();
                return Task.CompletedTask;
            });
        Application.Run(context);
        return 0;
    }

    private static int ReadPort(string variableName, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        return int.TryParse(value, out int port) && port is > 0 and <= 65535 ? port : fallback;
    }

    private static string ResolveDashboardDirectory()
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (File.Exists(Path.Combine(baseDirectory, "index.html")))
        {
            return baseDirectory;
        }

        string currentDirectory = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(currentDirectory, "index.html")))
        {
            return currentDirectory;
        }

        return baseDirectory;
    }

    private static string? ReadExternalTelemetrySource()
    {
        string? value = Environment.GetEnvironmentVariable("PCMONITOR_TELEMETRY_SOURCE");
        if (string.IsNullOrWhiteSpace(value))
        {
            value = Environment.GetEnvironmentVariable("LIBREHW_SOURCE");
        }

        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "PC Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

sealed class EmbeddedTelemetryCollector : IDisposable
{
    private readonly object syncRoot = new();
    private readonly TimeSpan cacheDuration = TimeSpan.FromMilliseconds(750);
    private Computer? computer;
    private string? cachedJson;
    private DateTimeOffset cachedAt;
    private int nextId;

    public string GetJson()
    {
        lock (syncRoot)
        {
            if (cachedJson is not null && DateTimeOffset.UtcNow - cachedAt < cacheDuration)
            {
                return cachedJson;
            }

            EnsureComputer();
            UpdateHardware();

            SensorNode root = BuildTree();
            cachedJson = JsonSerializer.Serialize(root, TelemetryJson.Options);
            cachedAt = DateTimeOffset.UtcNow;
            return cachedJson;
        }
    }

    public void Reset()
    {
        lock (syncRoot)
        {
            computer?.Close();
            computer = null;
            cachedJson = null;
            nextId = 0;
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            computer?.Close();
            computer = null;
        }
    }

    private void EnsureComputer()
    {
        if (computer is not null)
        {
            return;
        }

        computer = new Computer
        {
            IsBatteryEnabled = true,
            IsControllerEnabled = true,
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsNetworkEnabled = true,
            IsPsuEnabled = true,
            IsStorageEnabled = true
        };
        computer.Open();
    }

    private void UpdateHardware()
    {
        if (computer is null)
        {
            return;
        }

        foreach (IHardware hardware in computer.Hardware)
        {
            UpdateHardwareRecursive(hardware);
        }
    }

    private static void UpdateHardwareRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware subHardware in hardware.SubHardware)
        {
            UpdateHardwareRecursive(subHardware);
        }
    }

    private SensorNode BuildTree()
    {
        if (computer is null)
        {
            throw new InvalidOperationException("Telemetry collector was not initialized.");
        }

        nextId = 0;
        SensorNode machine = new()
        {
            id = NextId(),
            Text = Environment.MachineName,
            ImageURL = "images_computer.png"
        };

        foreach (IHardware hardware in computer.Hardware.OrderBy(static item => item.HardwareType.ToString()).ThenBy(static item => item.Name))
        {
            machine.Children.Add(BuildHardwareNode(hardware));
        }

        return new SensorNode
        {
            id = NextId(),
            Text = "LibreHardwareMonitor",
            ImageURL = "images_icon.png",
            Children = [machine]
        };
    }

    private SensorNode BuildHardwareNode(IHardware hardware)
    {
        SensorNode node = new()
        {
            id = NextId(),
            Text = hardware.Name,
            ImageURL = ImageForHardware(hardware.HardwareType)
        };

        foreach (IGrouping<SensorType, ISensor> group in hardware.Sensors
            .Where(static sensor => sensor.Value.HasValue)
            .OrderBy(static sensor => sensor.SensorType.ToString())
            .ThenBy(static sensor => sensor.Index)
            .GroupBy(static sensor => sensor.SensorType))
        {
            SensorNode groupNode = new()
            {
                id = NextId(),
                Text = GroupName(group.Key),
                ImageURL = ImageForSensorType(group.Key)
            };

            foreach (ISensor sensor in group)
            {
                groupNode.Children.Add(BuildSensorNode(sensor));
            }

            node.Children.Add(groupNode);
        }

        foreach (IHardware subHardware in hardware.SubHardware.OrderBy(static item => item.Name))
        {
            node.Children.Add(BuildHardwareNode(subHardware));
        }

        return node;
    }

    private SensorNode BuildSensorNode(ISensor sensor)
    {
        return new SensorNode
        {
            id = NextId(),
            Text = sensor.Name,
            Type = sensor.SensorType.ToString(),
            SensorId = sensor.Identifier.ToString(),
            Value = FormatSensorValue(sensor.Value, sensor.SensorType),
            Min = FormatSensorValue(sensor.Min, sensor.SensorType),
            Max = FormatSensorValue(sensor.Max, sensor.SensorType),
            ImageURL = ImageForSensorType(sensor.SensorType)
        };
    }

    private int NextId()
    {
        nextId += 1;
        return nextId;
    }

    private static string GroupName(SensorType type)
    {
        return type switch
        {
            SensorType.Clock => "Clocks",
            SensorType.Control => "Controls",
            SensorType.Data => "Data",
            SensorType.Factor => "Factors",
            SensorType.Fan => "Fans",
            SensorType.Flow => "Flows",
            SensorType.Frequency => "Frequencies",
            SensorType.Level => "Levels",
            SensorType.Load => "Load",
            SensorType.Power => "Powers",
            SensorType.SmallData => "Data",
            SensorType.Temperature => "Temperatures",
            SensorType.Throughput => "Throughput",
            SensorType.TimeSpan => "Times",
            SensorType.Voltage => "Voltages",
            _ => type.ToString()
        };
    }

    private static string FormatSensorValue(float? value, SensorType type)
    {
        if (!value.HasValue || float.IsNaN(value.Value) || float.IsInfinity(value.Value))
        {
            return string.Empty;
        }

        return type switch
        {
            SensorType.Clock => $"{FormatNumber(value.Value, 0)} MHz",
            SensorType.Control => $"{FormatNumber(value.Value, 1)} %",
            SensorType.Data => $"{FormatNumber(value.Value, 1)} GB",
            SensorType.Factor => $"{FormatNumber(value.Value, 2)} x",
            SensorType.Fan => $"{FormatNumber(value.Value, 0)} RPM",
            SensorType.Flow => $"{FormatNumber(value.Value, 1)} L/h",
            SensorType.Frequency => $"{FormatNumber(value.Value, 0)} Hz",
            SensorType.Level => $"{FormatNumber(value.Value, 1)} %",
            SensorType.Load => $"{FormatNumber(value.Value, 1)} %",
            SensorType.Power => $"{FormatNumber(value.Value, 1)} W",
            SensorType.SmallData => $"{FormatNumber(value.Value, 0)} MB",
            SensorType.Temperature => $"{FormatNumber(value.Value, 1)} °C",
            SensorType.Throughput => $"{FormatNumber(value.Value, 1)} KB/s",
            SensorType.TimeSpan => $"{FormatNumber(value.Value, 1)} s",
            SensorType.Voltage => $"{FormatNumber(value.Value, 3)} V",
            _ => FormatNumber(value.Value, 2)
        };
    }

    private static string FormatNumber(float value, int decimals)
    {
        string format = decimals <= 0 ? "0" : $"0.{new string('#', decimals)}";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string ImageForHardware(HardwareType type)
    {
        return type switch
        {
            HardwareType.Cpu => "images_cpu.png",
            HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia => "images_gpu.png",
            HardwareType.Memory => "images_ram.png",
            HardwareType.Motherboard => "images_mainboard.png",
            HardwareType.Network => "images_nic.png",
            HardwareType.Storage => "images_hdd.png",
            _ => "images_chip.png"
        };
    }

    private static string ImageForSensorType(SensorType type)
    {
        return type switch
        {
            SensorType.Clock => "images_clock.png",
            SensorType.Control => "images_control.png",
            SensorType.Data or SensorType.SmallData => "images_hdd.png",
            SensorType.Fan => "images_fan.png",
            SensorType.Load => "images_load.png",
            SensorType.Power => "images_power.png",
            SensorType.Temperature => "images_temperature.png",
            SensorType.Throughput => "images_throughput.png",
            SensorType.Voltage => "images_voltage.png",
            _ => "images_sensor.png"
        };
    }
}

sealed class DashboardServer : IDisposable
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".ico"] = "image/x-icon",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml"
    };

    private readonly string rootDirectory;
    private readonly int port;
    private readonly EmbeddedTelemetryCollector collector;
    private readonly string? externalTelemetrySource;
    private readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };
    private HttpListener? listener;
    private CancellationTokenSource? cancellation;
    private Task? serverTask;

    public DashboardServer(string rootDirectory, int port, EmbeddedTelemetryCollector collector, string? externalTelemetrySource)
    {
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        this.port = port;
        this.collector = collector;
        this.externalTelemetrySource = externalTelemetrySource;
    }

    public void Start()
    {
        listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        cancellation = new CancellationTokenSource();
        serverTask = Task.Run(() => RunAsync(cancellation.Token));
    }

    public void Dispose()
    {
        cancellation?.Cancel();
        listener?.Close();
        httpClient.Dispose();
        try
        {
            serverTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown is best effort; process exit will reclaim listener resources.
        }
        cancellation?.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        if (listener is null)
        {
            return;
        }

        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch when (token.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
                continue;
            }

            _ = Task.Run(() => HandleRequestAsync(context), token);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            if (path.Equals("/data.json", StringComparison.OrdinalIgnoreCase))
            {
                await ServeTelemetryAsync(context.Response);
                return;
            }

            await ServeStaticFileAsync(context.Response, path);
        }
        catch (Exception error)
        {
            await WriteJsonAsync(context.Response, 500, new
            {
                error = "Internal server error",
                detail = error.Message
            });
        }
    }

    private async Task ServeTelemetryAsync(HttpListenerResponse response)
    {
        if (!string.IsNullOrWhiteSpace(externalTelemetrySource))
        {
            try
            {
                using HttpResponseMessage upstreamResponse = await httpClient.GetAsync(externalTelemetrySource);
                string body = await upstreamResponse.Content.ReadAsStringAsync();
                if (!upstreamResponse.IsSuccessStatusCode || !TelemetryJson.LooksLikeLibreHardwarePayload(body))
                {
                    throw new InvalidOperationException($"Invalid external telemetry response: {(int)upstreamResponse.StatusCode}");
                }

                await WriteTextAsync(response, 200, "application/json; charset=utf-8", body, new Dictionary<string, string>
                {
                    ["X-PCMonitor-Telemetry"] = "external",
                    ["X-PCMonitor-Source"] = externalTelemetrySource
                });
                return;
            }
            catch (Exception error)
            {
                await WriteJsonAsync(response, 502, new
                {
                    error = "Failed to fetch external telemetry",
                    detail = error.Message,
                    source = externalTelemetrySource
                });
                return;
            }
        }

        string json = collector.GetJson();
        await WriteTextAsync(response, 200, "application/json; charset=utf-8", json, new Dictionary<string, string>
        {
            ["X-PCMonitor-Telemetry"] = "embedded"
        });
    }

    private async Task ServeStaticFileAsync(HttpListenerResponse response, string requestPath)
    {
        string relativePath = requestPath == "/" ? "index.html" : Uri.UnescapeDataString(requestPath.TrimStart('/'));
        string normalizedPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));

        if (!normalizedPath.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            await WriteTextAsync(response, 403, "text/plain; charset=utf-8", "Forbidden");
            return;
        }

        if (!File.Exists(normalizedPath))
        {
            await WriteTextAsync(response, 404, "text/plain; charset=utf-8", "Not found");
            return;
        }

        string extension = Path.GetExtension(normalizedPath);
        string contentType = MimeTypes.TryGetValue(extension, out string? mimeType)
            ? mimeType
            : "application/octet-stream";

        byte[] bytes = await File.ReadAllBytesAsync(normalizedPath);
        response.StatusCode = 200;
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        string json = JsonSerializer.Serialize(payload, TelemetryJson.Options);
        return WriteTextAsync(response, statusCode, "application/json; charset=utf-8", json);
    }

    private static async Task WriteTextAsync(
        HttpListenerResponse response,
        int statusCode,
        string contentType,
        string body,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        if (headers is not null)
        {
            foreach ((string key, string value) in headers)
            {
                response.Headers[key] = value;
            }
        }

        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}

static class TelemetryJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static bool LooksLikeLibreHardwarePayload(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("Children", out JsonElement children) &&
                children.ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }
}

sealed class SensorNode
{
    public int id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Min { get; set; }
    public string? Value { get; set; }
    public string? Max { get; set; }
    public string? Type { get; set; }
    public string? SensorId { get; set; }
    public string ImageURL { get; set; } = string.Empty;
    public List<SensorNode> Children { get; set; } = [];
}

sealed class DashboardAppContext : ApplicationContext
{
    private readonly Func<Task> restartTelemetry;
    private readonly NotifyIcon notifyIcon;
    private readonly DashboardForm form;
    private bool exiting;

    public DashboardAppContext(string dashboardUrl, Func<Task> restartTelemetry)
    {
        this.restartTelemetry = restartTelemetry;

        form = new DashboardForm(dashboardUrl, () => exiting);
        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PC Monitor",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        notifyIcon.DoubleClick += (_, _) => ShowDashboard();
        form.Show();
        form.Activate();
    }

    private ContextMenuStrip BuildMenu()
    {
        ContextMenuStrip menu = new();
        menu.Items.Add("Open", null, (_, _) => ShowDashboard());
        menu.Items.Add("Fullscreen", null, (_, _) => form.ToggleFullscreen());
        menu.Items.Add("Reload", null, (_, _) => form.ReloadDashboard());
        menu.Items.Add("Reset telemetry", null, async (_, _) => await RestartTelemetryAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        return menu;
    }

    private void ShowDashboard()
    {
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

    private async Task RestartTelemetryAsync()
    {
        notifyIcon.ShowBalloonTip(1000, "PC Monitor", "Resetting embedded telemetry collector...", ToolTipIcon.Info);
        await restartTelemetry();
        form.ReloadDashboard();
    }

    private void ExitApplication()
    {
        exiting = true;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        form.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            notifyIcon.Dispose();
            form.Dispose();
        }

        base.Dispose(disposing);
    }
}

sealed class DashboardForm : Form
{
    private readonly string dashboardUrl;
    private readonly Func<bool> isExiting;
    private readonly WebView2 webView = new();
    private bool fullscreen;
    private Rectangle restoreBounds;
    private FormBorderStyle restoreBorderStyle;
    private FormWindowState restoreWindowState;

    public DashboardForm(string dashboardUrl, Func<bool> isExiting)
    {
        this.dashboardUrl = dashboardUrl;
        this.isExiting = isExiting;

        Text = "PC Monitor";
        Width = 1600;
        Height = 950;
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        webView.Dock = DockStyle.Fill;
        Controls.Add(webView);

        FormClosing += OnFormClosing;
        KeyDown += OnKeyDown;
        Shown += async (_, _) => await InitializeWebViewAsync();
    }

    public void ReloadDashboard()
    {
        if (webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.Reload();
        }
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
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LibreHWStats",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            webView.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = userDataFolder
            };

            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Navigate(dashboardUrl);
        }
        catch (Exception error)
        {
            MessageBox.Show(
                $"WebView2 could not start.\n\n{error.Message}",
                "PC Monitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (isExiting())
        {
            return;
        }

        args.Cancel = true;
        Hide();
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyCode == Keys.F11)
        {
            ToggleFullscreen();
        }
        else if (args.KeyCode == Keys.Escape && fullscreen)
        {
            ToggleFullscreen();
        }
    }
}
