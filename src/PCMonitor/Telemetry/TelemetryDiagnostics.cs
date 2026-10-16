using PCMonitor.Settings;

namespace PCMonitor.Telemetry;

internal static class TelemetryDiagnostics
{
    public static IReadOnlyList<string> Problems(
        TelemetrySnapshot snapshot, AppSettings settings, bool elevated,
        DriverStatus? driver, IReadOnlyList<string> sourceProblems)
    {
        List<string> problems = [.. sourceProblems];
        if (!elevated)
            problems.Add("Not running as Administrator: CPU, fan and motherboard sensors are limited");
        if ((settings.EnableCpu || settings.EnableMotherboard) && driver is not null)
        {
            if (driver.Error is not null)
                problems.Add($"PawnIO detection failed: {driver.Error}");
            else if (!driver.Installed)
                problems.Add("PawnIO is not installed. Install it from https://pawnio.eu and restart PC Monitor for CPU and motherboard sensor access");
        }

        if (!snapshot.Sensors.Any(s => s.Value.HasValue))
            problems.Add("No readable sensors reported (driver blocked or hardware unsupported)");

        if (settings.EnableCpu)
        {
            HardwareInfo[] cpus = [.. snapshot.Hardware.Where(h => h.Type == "Cpu")];
            if (cpus.Length == 0)
                problems.Add("CPU not detected by the hardware library");
            foreach (HardwareInfo cpu in cpus)
            {
                if (!snapshot.Sensors.Any(s => s.Device == cpu.Name && s.Type == "Temperature" && s.Value is > 0))
                    problems.Add($"CPU temperature unavailable: {cpu.Name}. Check PawnIO, driver access and hardware support");
            }
        }

        if (settings.EnableMotherboard)
        {
            HardwareInfo[] boards = [.. snapshot.Hardware.Where(h => h.Type == "Motherboard")];
            if (boards.Length == 0)
                problems.Add("Motherboard not detected by the hardware library");
            foreach (HardwareInfo board in boards)
            {
                if (!snapshot.Sensors.Any(s => s.Device == board.Name && s.Value.HasValue))
                    problems.Add($"Motherboard sensors unavailable: {board.Name}. Check PawnIO, driver access and board support");
            }
        }
        return problems.Distinct(StringComparer.Ordinal).ToArray();
    }
}
