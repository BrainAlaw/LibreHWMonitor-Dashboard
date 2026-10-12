namespace PCMonitor.Telemetry;

/// <summary>Short status line for the tray tooltip (limited to 63 characters by Windows).</summary>
internal static class TraySummary
{
    private const string Title = "PC Monitor";

    public static string Build(TelemetrySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return Title;
        }

        IEnumerable<SensorReading> cpu = snapshot.Sensors.Where(IsCpu);
        double? load = cpu.FirstOrDefault(static s => s is { Type: "Load", Name: "CPU Total" })?.Value;
        double? temperature = cpu
            .Where(static s => s.Type == "Temperature" && s.Value is > 0)
            .OrderByDescending(static s => IsPreferredTemperature(s.Name))
            .ThenByDescending(static s => s.Value)
            .Select(static s => s.Value)
            .FirstOrDefault();

        string text = Title;
        if (load.HasValue)
        {
            text += $" | CPU {load.Value:0}%";
        }

        if (temperature.HasValue)
        {
            text += $" {temperature.Value:0}°C";
        }

        return text.Length <= 63 ? text : text[..63];
    }

    private static bool IsCpu(SensorReading sensor) =>
        sensor.Id.Contains("cpu", StringComparison.OrdinalIgnoreCase);

    private static bool IsPreferredTemperature(string name) =>
        name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tdie", StringComparison.OrdinalIgnoreCase);
}
