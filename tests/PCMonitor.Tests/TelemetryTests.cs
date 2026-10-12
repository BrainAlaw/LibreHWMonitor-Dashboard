using PCMonitor.Telemetry;
using Xunit;

namespace PCMonitor.Tests;

public sealed class SnapshotBuilderTests
{
    private static RawSensor Sensor(string id, string name, string type, float? value, int index = 0, float? min = null, float? max = null) =>
        new(id, name, type, index, value, min, max);

    private static RawHardware Hardware(string name, string type, RawSensor[]? sensors = null, RawHardware[]? children = null) =>
        new(name, type, sensors ?? [], children ?? []);

    [Fact]
    public void SkipsSensorsWithoutFiniteValues()
    {
        RawHardware cpu = Hardware("CPU", "Cpu",
        [
            Sensor("/cpu/load/0", "CPU Total", "Load", 12.34f),
            Sensor("/cpu/load/1", "Missing", "Load", null),
            Sensor("/cpu/load/2", "NaN", "Load", float.NaN),
            Sensor("/cpu/load/3", "Inf", "Load", float.PositiveInfinity)
        ]);

        TelemetrySnapshot snapshot = SnapshotBuilder.Build(DateTimeOffset.UnixEpoch, [cpu]);

        SensorReading only = Assert.Single(snapshot.Sensors);
        Assert.Equal("CPU Total", only.Name);
    }

    [Fact]
    public void AttachesUnitGroupAndRoundsToPrecision()
    {
        RawHardware cpu = Hardware("CPU", "Cpu", [Sensor("/cpu/temp/0", "Core", "Temperature", 63.456789f, max: 90.04f)]);

        SensorReading reading = Assert.Single(SnapshotBuilder.Build(DateTimeOffset.UnixEpoch, [cpu]).Sensors);

        Assert.Equal("°C", reading.Unit);
        Assert.Equal("Temperatures", reading.Group);
        Assert.Equal(63.46, reading.Value);
        Assert.Equal(90.04, reading.Max);
        Assert.Null(reading.Min);
    }

    [Fact]
    public void KeepsThroughputInBytesPerSecond()
    {
        RawHardware nic = Hardware("Ethernet", "Network", [Sensor("/nic/x/throughput/7", "Download Speed", "Throughput", 2_097_152f)]);

        SensorReading reading = Assert.Single(SnapshotBuilder.Build(DateTimeOffset.UnixEpoch, [nic]).Sensors);

        Assert.Equal("B/s", reading.Unit);
        Assert.Equal(2_097_152d, reading.Value);
    }

    [Fact]
    public void FlattensSubHardwareUnderTheTopLevelDevice()
    {
        RawHardware board = Hardware("ASUS Board", "Motherboard",
            children: [Hardware("ITE IT8665E", "SuperIO", [Sensor("/lpc/it8665e/0/fan/0", "Fan #1", "Fan", 1000f)])]);

        TelemetrySnapshot snapshot = SnapshotBuilder.Build(DateTimeOffset.UnixEpoch, [board]);

        SensorReading fan = Assert.Single(snapshot.Sensors);
        Assert.Equal("ASUS Board", fan.Device);
        Assert.Equal(["ITE IT8665E"], fan.Path);
        Assert.Equal(["ASUS Board", "ITE IT8665E"], snapshot.Hardware.Select(h => h.Name));
    }

    [Fact]
    public void OrdersDevicesAndSensorsDeterministically()
    {
        RawHardware gpu = Hardware("GPU", "GpuNvidia", [Sensor("/g/2", "B", "Load", 1f, index: 2), Sensor("/g/1", "A", "Load", 1f, index: 1)]);
        RawHardware cpu = Hardware("CPU", "Cpu", [Sensor("/c/0", "C", "Load", 1f)]);

        TelemetrySnapshot snapshot = SnapshotBuilder.Build(DateTimeOffset.UnixEpoch, [gpu, cpu]);

        Assert.Equal(["/c/0", "/g/1", "/g/2"], snapshot.Sensors.Select(s => s.Id));
    }

    [Fact]
    public void UnknownSensorTypesFallBackToTwoDecimalsWithoutUnit()
    {
        SensorMetadata.Info info = SensorMetadata.For("SomethingNew");

        Assert.Equal("SomethingNew", info.Group);
        Assert.Equal(string.Empty, info.Unit);
        Assert.Equal(2, info.Decimals);
    }
}

public sealed class TraySummaryTests
{
    private static SensorReading Reading(string id, string name, string type, double value) =>
        new(id, "CPU", [], "g", name, type, value, null, null, "", 1);

    [Fact]
    public void ShowsLoadAndPreferredTemperature()
    {
        TelemetrySnapshot snapshot = new(DateTimeOffset.UnixEpoch, [],
        [
            Reading("/amdcpu/0/load/0", "CPU Total", "Load", 87.2),
            Reading("/amdcpu/0/temperature/1", "Core #1", "Temperature", 90),
            Reading("/amdcpu/0/temperature/0", "Core (Tctl/Tdie)", "Temperature", 76)
        ]);

        Assert.Equal("PC Monitor | CPU 87% 76°C", TraySummary.Build(snapshot));
    }

    [Fact]
    public void FallsBackToTitleWithoutData()
    {
        Assert.Equal("PC Monitor", TraySummary.Build(null));
    }
}
