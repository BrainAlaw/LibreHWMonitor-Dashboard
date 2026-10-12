namespace PCMonitor.Infra;

/// <summary>
/// Windows Firewall rule that lets private-network clients reach the dashboard.
/// The rule is program-based and limited to the Private profile, so it follows the port.
/// </summary>
internal static class Firewall
{
    private const string RuleName = "PC Monitor LAN";

    public static bool Apply(bool enable, string exePath)
    {
        // Always remove first so a moved executable or changed profile never leaves a stale rule behind.
        ProcessRunner.Run("netsh.exe", "advfirewall", "firewall", "delete", "rule", $"name={RuleName}");
        if (!enable)
        {
            return true;
        }

        return ProcessRunner.Run(
            "netsh.exe", "advfirewall", "firewall", "add", "rule",
            $"name={RuleName}", "dir=in", "action=allow", "protocol=TCP",
            $"program={exePath}", "profile=private", "enable=yes").ExitCode == 0;
    }
}
