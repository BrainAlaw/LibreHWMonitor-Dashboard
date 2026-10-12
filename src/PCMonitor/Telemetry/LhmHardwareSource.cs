using LibreHardwareMonitor.Hardware;
using PCMonitor.Infra;
using PCMonitor.Settings;

namespace PCMonitor.Telemetry;

/// <summary>Adapter over LibreHardwareMonitor's <see cref="Computer"/>.</summary>
internal sealed class LhmHardwareSource : IHardwareSource
{
    private readonly Computer computer;

    public LhmHardwareSource(AppSettings settings)
    {
        computer = new Computer
        {
            IsBatteryEnabled = settings.EnableBattery,
            IsControllerEnabled = settings.EnableController,
            IsCpuEnabled = settings.EnableCpu,
            IsGpuEnabled = settings.EnableGpu,
            IsMemoryEnabled = settings.EnableMemory,
            IsMotherboardEnabled = settings.EnableMotherboard,
            IsNetworkEnabled = settings.EnableNetwork,
            IsPsuEnabled = settings.EnablePsu,
            IsStorageEnabled = settings.EnableStorage
        };
        computer.Open();
        Log.Info($"Hardware opened: {computer.Hardware.Count} devices");
    }

    public int HardwareCount => computer.Hardware.Count;

    public IReadOnlyList<RawHardware> Poll()
    {
        foreach (IHardware hardware in computer.Hardware)
        {
            UpdateRecursive(hardware);
        }

        return [.. computer.Hardware.Select(Convert)];
    }

    public void Dispose()
    {
        try
        {
            computer.Close();
        }
        catch (Exception error)
        {
            Log.Error("Computer.Close failed", error);
        }
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware child in hardware.SubHardware)
        {
            UpdateRecursive(child);
        }
    }

    private static RawHardware Convert(IHardware hardware) => new(
        hardware.Name,
        hardware.HardwareType.ToString(),
        [.. hardware.Sensors.Select(static s => new RawSensor(
            s.Identifier.ToString(), s.Name, s.SensorType.ToString(), s.Index, s.Value, s.Min, s.Max))],
        [.. hardware.SubHardware.Select(Convert)]);
}
