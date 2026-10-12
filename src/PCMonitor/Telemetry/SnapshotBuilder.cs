namespace PCMonitor.Telemetry;

/// <summary>Pure conversion from raw hardware trees to the dashboard snapshot.</summary>
internal static class SnapshotBuilder
{
    public static TelemetrySnapshot Build(DateTimeOffset timestamp, IEnumerable<RawHardware> devices)
    {
        List<HardwareInfo> hardware = [];
        List<SensorReading> sensors = [];

        foreach (RawHardware device in devices.OrderBy(static d => d.Type, StringComparer.Ordinal).ThenBy(static d => d.Name, StringComparer.Ordinal))
        {
            Visit(device, device.Name, [], hardware, sensors);
        }

        return new TelemetrySnapshot(timestamp, hardware, sensors);
    }

    private static void Visit(
        RawHardware node,
        string device,
        IReadOnlyList<string> path,
        List<HardwareInfo> hardware,
        List<SensorReading> sensors)
    {
        hardware.Add(new HardwareInfo(node.Name, node.Type));

        foreach (RawSensor sensor in node.Sensors
            .Where(static s => SensorMetadata.IsFinite(s.Value))
            .OrderBy(static s => s.Type, StringComparer.Ordinal)
            .ThenBy(static s => s.Index))
        {
            SensorMetadata.Info info = SensorMetadata.For(sensor.Type);
            sensors.Add(new SensorReading(
                sensor.Identifier,
                device,
                path,
                info.Group,
                sensor.Name,
                sensor.Type,
                Round(sensor.Value, info.Decimals),
                Round(sensor.Min, info.Decimals),
                Round(sensor.Max, info.Decimals),
                info.Unit,
                info.Decimals));
        }

        foreach (RawHardware child in node.Children.OrderBy(static c => c.Name, StringComparer.Ordinal))
        {
            Visit(child, device, [.. path, child.Name], hardware, sensors);
        }
    }

    private static double? Round(float? value, int decimals) =>
        SensorMetadata.IsFinite(value) ? Math.Round(value!.Value, decimals + 1, MidpointRounding.AwayFromZero) : null;
}
