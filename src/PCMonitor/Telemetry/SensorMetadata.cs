namespace PCMonitor.Telemetry;

internal static class SensorMetadata
{
    internal readonly record struct Info(string Group, string Unit, int Decimals);

    private static readonly Dictionary<string, Info> Table = new(StringComparer.Ordinal)
    {
        ["Clock"] = new("Clocks", "MHz", 1),
        ["Conductivity"] = new("Conductivity", "µS/cm", 1),
        ["Control"] = new("Controls", "%", 1),
        ["Current"] = new("Currents", "A", 3),
        ["Data"] = new("Data", "GB", 1),
        ["Energy"] = new("Energy", "mWh", 0),
        ["Factor"] = new("Factors", string.Empty, 3),
        ["Fan"] = new("Fans", "RPM", 0),
        ["Flow"] = new("Flows", "L/h", 1),
        ["Frequency"] = new("Frequencies", "Hz", 0),
        ["Humidity"] = new("Humidity", "%", 1),
        ["Level"] = new("Levels", "%", 1),
        ["Load"] = new("Load", "%", 1),
        ["Noise"] = new("Noise", "dBA", 1),
        ["Power"] = new("Powers", "W", 1),
        ["SmallData"] = new("Data", "MB", 0),
        ["Temperature"] = new("Temperatures", "°C", 1),
        ["Throughput"] = new("Throughput", "B/s", 0),
        ["TimeSpan"] = new("Times", "s", 1),
        ["Timing"] = new("Timings", "ns", 3),
        ["Voltage"] = new("Voltages", "V", 3)
    };

    public static Info For(string sensorType) =>
        Table.TryGetValue(sensorType, out Info info) ? info : new Info(sensorType, string.Empty, 2);

    public static bool IsFinite(float? value) =>
        value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value);
}
