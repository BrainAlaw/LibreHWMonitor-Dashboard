namespace PCMonitor.Telemetry;

/// <summary>Hardware-library-agnostic input: what a hardware source reports on each poll.</summary>
internal sealed record RawSensor(string Identifier, string Name, string Type, int Index, float? Value, float? Min, float? Max);

internal sealed record RawHardware(
    string Name,
    string Type,
    IReadOnlyList<RawSensor> Sensors,
    IReadOnlyList<RawHardware> Children);

/// <summary>One hardware device (top level or nested), as exposed to the dashboard.</summary>
internal sealed record HardwareInfo(string Name, string Type);

/// <summary>
/// One sensor reading. Values are raw numbers in the unit given by <see cref="Unit"/>
/// (throughput is bytes per second); formatting is the client's job.
/// </summary>
internal sealed record SensorReading(
    string Id,
    string Device,
    IReadOnlyList<string> Path,
    string Group,
    string Name,
    string Type,
    double? Value,
    double? Min,
    double? Max,
    string Unit,
    int Decimals);

internal sealed record TelemetrySnapshot(
    string Host,
    DateTimeOffset Timestamp,
    IReadOnlyList<HardwareInfo> Hardware,
    IReadOnlyList<SensorReading> Sensors);

internal sealed record TelemetryHealth(
    string Status,
    bool Elevated,
    int HardwareCount,
    int SensorCount,
    string? LastError,
    DateTimeOffset? LastUpdate);

/// <summary>An immutable published snapshot together with its pre-serialized JSON.</summary>
internal sealed record PublishedSnapshot(long Version, TelemetrySnapshot Snapshot, string Json);

internal interface IHardwareSource : IDisposable
{
    int HardwareCount { get; }

    /// <summary>Refreshes all sensors and returns the current device tree.</summary>
    IReadOnlyList<RawHardware> Poll();
}
