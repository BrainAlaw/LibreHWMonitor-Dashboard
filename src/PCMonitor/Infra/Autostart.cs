using System.Diagnostics;
using System.Security;
using System.Text;

namespace PCMonitor.Infra;

/// <summary>
/// Autostart through a Scheduled Task so the app starts elevated at logon without a UAC prompt.
/// </summary>
internal static class Autostart
{
    private const string TaskName = "PCMonitor";

    public static bool IsEnabled() => Run("/Query", "/TN", TaskName).ExitCode == 0;

    public static bool Enable(string exePath)
    {
        string user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}") ?? string.Empty;
        string command = SecurityElement.Escape(exePath) ?? string.Empty;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{command}</Command><Arguments>--minimized</Arguments></Exec>
              </Actions>
            </Task>
            """;

        string xmlFile = Path.Combine(Path.GetTempPath(), $"pcmonitor-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlFile, xml, Encoding.Unicode);
            return Run("/Create", "/TN", TaskName, "/XML", xmlFile, "/F").ExitCode == 0;
        }
        finally
        {
            try
            {
                File.Delete(xmlFile);
            }
            catch
            {
                // Temp file cleanup is best effort.
            }
        }
    }

    public static bool Disable() => !IsEnabled() || Run("/Delete", "/TN", TaskName, "/F").ExitCode == 0;

    private static (int ExitCode, string Output) Run(params string[] arguments)
    {
        ProcessStartInfo startInfo = new("schtasks.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(10_000);
            if (process.ExitCode != 0)
            {
                Log.Info($"schtasks {string.Join(' ', arguments)} -> {process.ExitCode}: {output.Trim()}");
            }

            return (process.ExitCode, output);
        }
        catch (Exception error)
        {
            Log.Error("schtasks failed", error);
            return (-1, error.Message);
        }
    }
}
