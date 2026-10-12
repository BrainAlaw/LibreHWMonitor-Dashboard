using System.Text.Json;
using LibreHardwareMonitor.Hardware;
using PCMonitor.Infra;
using PCMonitor.Settings;

namespace PCMonitor.Telemetry;

internal sealed record TelemetryHealth(
    string Status,
    string Mode,
    bool Elevated,
    int HardwareCount,
    int SensorCount,
    string? LastError,
    DateTimeOffset? LastUpdate);

/// <summary>
/// Owns the LibreHardwareMonitor <see cref="Computer"/>. A single background loop polls hardware
/// and publishes an immutable JSON snapshot, so HTTP requests never touch hardware.
/// </summary>
internal sealed class TelemetryCollector : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly SettingsStore settings;
    private readonly bool elevated = Elevation.IsElevated();
    private readonly CancellationTokenSource cancellation = new();
    private volatile bool resetRequested;
    private volatile string snapshotJson = "{\"id\":0,\"Text\":\"PC Monitor\",\"Children\":[]}";
    private volatile string summary = "PC Monitor";
    private TelemetryHealth health;
    private Task? loop;

    public TelemetryCollector(SettingsStore settings)
    {
        this.settings = settings;
        health = new TelemetryHealth("starting", "embedded", elevated, 0, 0, null, null);
    }

    public string SnapshotJson => snapshotJson;
    public string Summary => summary;
    public TelemetryHealth Health => Volatile.Read(ref health);

    public void Start()
    {
        loop = Task.Run(() => RunAsync(cancellation.Token));
    }

    public void Reset() => resetRequested = true;

    public void Dispose()
    {
        cancellation.Cancel();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Shutdown is best effort.
        }

        cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        Computer? computer = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan delay = TimeSpan.FromMilliseconds(settings.Current.SamplingMs);
                try
                {
                    if (resetRequested)
                    {
                        resetRequested = false;
                        CloseComputer(ref computer);
                        Log.Info("Collector reset requested");
                    }

                    computer ??= OpenComputer();
                    Sample(computer);
                }
                catch (Exception error)
                {
                    Log.Error("Telemetry sampling failed", error);
                    CloseComputer(ref computer);
                    PublishFailure(error);
                    delay = RetryDelay;
                }

                await Task.Delay(delay, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            CloseComputer(ref computer);
        }
    }

    private Computer OpenComputer()
    {
        AppSettings config = settings.Current;
        Computer computer = new()
        {
            IsBatteryEnabled = config.EnableBattery,
            IsControllerEnabled = config.EnableController,
            IsCpuEnabled = config.EnableCpu,
            IsGpuEnabled = config.EnableGpu,
            IsMemoryEnabled = config.EnableMemory,
            IsMotherboardEnabled = config.EnableMotherboard,
            IsNetworkEnabled = config.EnableNetwork,
            IsPsuEnabled = config.EnablePsu,
            IsStorageEnabled = config.EnableStorage
        };
        computer.Open();
        Log.Info($"Collector opened: {computer.Hardware.Count} hardware items, elevated={elevated}");
        return computer;
    }

    private static void CloseComputer(ref Computer? computer)
    {
        try
        {
            computer?.Close();
        }
        catch (Exception error)
        {
            Log.Error("Computer.Close failed", error);
        }

        computer = null;
    }

    private void Sample(Computer computer)
    {
        foreach (IHardware hardware in computer.Hardware)
        {
            UpdateRecursive(hardware);
        }

        int nextId = 0;
        int sensorCount = 0;
        SensorNode machine = new()
        {
            Id = ++nextId,
            Text = Environment.MachineName,
            ImageURL = "images_computer.png"
        };

        foreach (IHardware hardware in computer.Hardware
            .OrderBy(static item => item.HardwareType.ToString())
            .ThenBy(static item => item.Name))
        {
            machine.Children.Add(BuildHardware(hardware, ref nextId, ref sensorCount));
        }

        SensorNode root = new()
        {
            Id = ++nextId,
            Text = "LibreHardwareMonitor",
            ImageURL = "images_icon.png",
            Children = [machine]
        };

        // Root id is assigned last in the legacy layout; keep ids unique, order is irrelevant to the UI.
        snapshotJson = JsonSerializer.Serialize(root, TelemetryJson.Options);
        summary = BuildSummary(computer);

        bool degraded = sensorCount == 0 || !elevated;
        Volatile.Write(ref health, new TelemetryHealth(
            degraded ? "degraded" : "ok",
            "embedded",
            elevated,
            computer.Hardware.Count,
            sensorCount,
            sensorCount == 0 ? "No sensors reported (driver blocked or hardware unsupported)" :
                !elevated ? "Not running as Administrator: CPU, fan and motherboard sensors are limited" : null,
            DateTimeOffset.UtcNow));
    }

    private void PublishFailure(Exception error)
    {
        TelemetryHealth previous = Health;
        Volatile.Write(ref health, new TelemetryHealth(
            "error",
            "embedded",
            elevated,
            0,
            0,
            error.Message,
            previous.LastUpdate));
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware subHardware in hardware.SubHardware)
        {
            UpdateRecursive(subHardware);
        }
    }

    private static SensorNode BuildHardware(IHardware hardware, ref int nextId, ref int sensorCount)
    {
        SensorNode node = new()
        {
            Id = ++nextId,
            Text = hardware.Name,
            ImageURL = SensorFormatting.ImageForHardware(hardware.HardwareType)
        };

        foreach (IGrouping<SensorType, ISensor> group in hardware.Sensors
            .Where(static sensor => sensor.Value.HasValue)
            .OrderBy(static sensor => sensor.SensorType.ToString())
            .ThenBy(static sensor => sensor.Index)
            .GroupBy(static sensor => sensor.SensorType))
        {
            SensorNode groupNode = new()
            {
                Id = ++nextId,
                Text = SensorFormatting.GroupName(group.Key),
                ImageURL = SensorFormatting.ImageForSensorType(group.Key)
            };

            foreach (ISensor sensor in group)
            {
                sensorCount++;
                (string unit, _) = SensorFormatting.UnitAndPrecision(sensor.SensorType);
                groupNode.Children.Add(new SensorNode
                {
                    Id = ++nextId,
                    Text = sensor.Name,
                    Type = sensor.SensorType.ToString(),
                    SensorId = sensor.Identifier.ToString(),
                    Value = SensorFormatting.FormatValue(sensor.Value, sensor.SensorType),
                    Min = SensorFormatting.FormatValue(sensor.Min, sensor.SensorType),
                    Max = SensorFormatting.FormatValue(sensor.Max, sensor.SensorType),
                    ValueNum = SensorFormatting.Numeric(sensor.Value, sensor.SensorType),
                    MinNum = SensorFormatting.Numeric(sensor.Min, sensor.SensorType),
                    MaxNum = SensorFormatting.Numeric(sensor.Max, sensor.SensorType),
                    Unit = unit,
                    ImageURL = SensorFormatting.ImageForSensorType(sensor.SensorType)
                });
            }

            node.Children.Add(groupNode);
        }

        foreach (IHardware subHardware in hardware.SubHardware.OrderBy(static item => item.Name))
        {
            node.Children.Add(BuildHardware(subHardware, ref nextId, ref sensorCount));
        }

        return node;
    }

    private static string BuildSummary(Computer computer)
    {
        IHardware? cpu = computer.Hardware.FirstOrDefault(static item => item.HardwareType == HardwareType.Cpu);
        if (cpu is null)
        {
            return "PC Monitor";
        }

        float? temperature = cpu.Sensors
            .Where(static s => s.SensorType == SensorType.Temperature && SensorFormatting.IsFinite(s.Value))
            .OrderByDescending(static s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                                           s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                                           s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase))
            .Select(static s => s.Value)
            .FirstOrDefault();
        float? load = cpu.Sensors
            .FirstOrDefault(static s => s.SensorType == SensorType.Load && s.Name == "CPU Total")?.Value;

        string text = "PC Monitor";
        if (SensorFormatting.IsFinite(load))
        {
            text += $" | CPU {load!.Value:0}%";
        }

        if (SensorFormatting.IsFinite(temperature))
        {
            text += $" {temperature!.Value:0}°C";
        }

        return text;
    }
}
