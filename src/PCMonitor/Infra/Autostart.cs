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

    private static (int ExitCode, string Output) Run(params string[] arguments) =>
        ProcessRunner.Run("schtasks.exe", arguments);
}
