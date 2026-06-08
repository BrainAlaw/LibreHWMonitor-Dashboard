using System.Diagnostics;
using System.Net.Sockets;

const int defaultPort = 8096;
int port = ReadPort(defaultPort);
string dashboardDirectory = ResolveDashboardDirectory();
string serverPath = Path.Combine(dashboardDirectory, "server.js");
string url = $"http://127.0.0.1:{port}/";

if (!File.Exists(serverPath))
{
    ShowError($"server.js was not found in:\n{dashboardDirectory}");
    return 1;
}

if (!IsPortOpen(port))
{
    StartServer(dashboardDirectory, serverPath, port);
}

if (!WaitForServer(port, TimeSpan.FromSeconds(8)))
{
    ShowError($"Dashboard server did not start on port {port}.\nCheck that Node.js is installed and LibreHW Monitor is running.");
    return 1;
}

OpenBrowser(url);
return 0;

static int ReadPort(int fallback)
{
    string? value = Environment.GetEnvironmentVariable("PORT");
    return int.TryParse(value, out int port) && port > 0 ? port : fallback;
}

static string ResolveDashboardDirectory()
{
    string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    if (File.Exists(Path.Combine(baseDirectory, "server.js")))
    {
        return baseDirectory;
    }

    string currentDirectory = Directory.GetCurrentDirectory();
    if (File.Exists(Path.Combine(currentDirectory, "server.js")))
    {
        return currentDirectory;
    }

    return baseDirectory;
}

static bool IsPortOpen(int port)
{
    try
    {
        using TcpClient client = new();
        IAsyncResult result = client.BeginConnect("127.0.0.1", port, null, null);
        bool connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
        if (!connected)
        {
            return false;
        }

        client.EndConnect(result);
        return true;
    }
    catch
    {
        return false;
    }
}

static void StartServer(string dashboardDirectory, string serverPath, int port)
{
    ProcessStartInfo startInfo = new()
    {
        FileName = "node",
        Arguments = $"\"{serverPath}\"",
        WorkingDirectory = dashboardDirectory,
        CreateNoWindow = true,
        UseShellExecute = false
    };

    startInfo.Environment["PORT"] = port.ToString();
    Process.Start(startInfo);
}

static bool WaitForServer(int port, TimeSpan timeout)
{
    Stopwatch stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < timeout)
    {
        if (IsPortOpen(port))
        {
            return true;
        }

        Thread.Sleep(250);
    }

    return false;
}

static void OpenBrowser(string url)
{
    ProcessStartInfo startInfo = new()
    {
        FileName = url,
        UseShellExecute = true
    };

    Process.Start(startInfo);
}

static void ShowError(string message)
{
    ProcessStartInfo startInfo = new()
    {
        FileName = "powershell",
        Arguments = $"-NoProfile -Command \"Add-Type -AssemblyName PresentationFramework; [System.Windows.MessageBox]::Show('{EscapeForPowerShell(message)}', 'LibreHW Dashboard')\"",
        CreateNoWindow = true,
        UseShellExecute = false
    };

    try
    {
        Process.Start(startInfo);
    }
    catch
    {
        Console.Error.WriteLine(message);
    }
}

static string EscapeForPowerShell(string value)
{
    return value.Replace("'", "''").Replace("\r", "").Replace("\n", "\\n");
}
