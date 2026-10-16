using PCMonitor.Settings;
using PCMonitor.Telemetry;
using Xunit;

namespace PCMonitor.Tests;

public sealed class TelemetryDiagnosticsTests
{
    private static readonly AppSettings Enabled = new();
    private static readonly DriverStatus Installed = new(true, "2.2.0");
    private static TelemetrySnapshot Snapshot(float? temperature = 55) => SnapshotBuilder.Build(DateTimeOffset.UnixEpoch,
    [
        new RawHardware("Intel CPU", "Cpu", [new RawSensor("/intelcpu/0/temperature/0", "CPU Package", "Temperature", 0, temperature, null, null)], []),
        new RawHardware("ASUS Board", "Motherboard", [], [
            new RawHardware("Nuvoton", "SuperIO", [new RawSensor("/lpc/nct/0/fan/0", "CPU Fan", "Fan", 0, 800, null, null)], [])])
    ]);

    [Fact]
    public void HealthyCpuAndBoardHaveNoProblems() =>
        Assert.Empty(TelemetryDiagnostics.Problems(Snapshot(), Enabled, true, Installed, []));

    [Fact]
    public void ReportsMissingPawnIoEvenWhenOtherSensorsWork()
    {
        var problems = TelemetryDiagnostics.Problems(Snapshot(null), Enabled, true, new DriverStatus(false, null), []);
        Assert.Contains(problems, p => p.Contains("PawnIO is not installed"));
        Assert.Contains(problems, p => p.Contains("CPU temperature unavailable"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0f)]
    public void MissingOrZeroCpuTemperatureIsDegraded(float? temperature) =>
        Assert.Contains(TelemetryDiagnostics.Problems(Snapshot(temperature), Enabled, true, Installed, []),
            p => p.Contains("CPU temperature unavailable"));

    [Fact]
    public void DisabledHardwareDoesNotProduceMissingHardwareWarnings()
    {
        var settings = new AppSettings { EnableCpu = false, EnableMotherboard = false };
        var snapshot = SnapshotBuilder.Build(DateTimeOffset.UnixEpoch,
            [new RawHardware("RAM", "Memory", [new RawSensor("/ram/load/0", "Memory", "Load", 0, 20, null, null)], [])]);
        Assert.Empty(TelemetryDiagnostics.Problems(snapshot, settings, true, new DriverStatus(false, null), []));
    }

    [Fact]
    public void AllUnavailableSensorsDoNotCountAsWorkingTelemetry()
    {
        var snapshot = SnapshotBuilder.Build(DateTimeOffset.UnixEpoch,
            [new RawHardware("CPU", "Cpu", [new RawSensor("/intelcpu/0/temperature/0", "CPU Package", "Temperature", 0, null, null, null)], [])]);
        Assert.Contains(TelemetryDiagnostics.Problems(snapshot, Enabled, true, Installed, []), p => p.Contains("No readable sensors"));
    }

    [Fact]
    public void PreservesDriverDetectionAndPerDeviceErrors()
    {
        var problems = TelemetryDiagnostics.Problems(Snapshot(), Enabled, true,
            new DriverStatus(false, null, "Access denied"), ["Sensor update failed: GPU"]);
        Assert.Contains("Sensor update failed: GPU", problems);
        Assert.Contains("PawnIO detection failed: Access denied", problems);
    }
}
