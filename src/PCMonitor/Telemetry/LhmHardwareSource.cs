using System.Collections.Concurrent;
using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using PCMonitor.Infra;
using PCMonitor.Settings;

namespace PCMonitor.Telemetry;

/// <summary>
/// Adapter over LibreHardwareMonitor's <see cref="Computer"/>.
/// Some devices (every storage drive: about 2 s per update) are far slower than the sampling interval.
/// Those run in a background "slow lane", one task per device and at most every <see cref="SlowInterval"/>,
/// so they never hold up the fast sensors. Their last known values are served in between.
/// </summary>
internal sealed class LhmHardwareSource : IHardwareSource
{
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(1000);

    private readonly Computer computer;
    private readonly HashSet<IHardware> slowLane = [];
    private readonly ConcurrentDictionary<IHardware, Task> running = new();
    private readonly ConcurrentDictionary<IHardware, long> lastFinished = new();

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
            if (hardware.HardwareType == HardwareType.Storage || slowLane.Contains(hardware))
            {
                slowLane.Add(hardware);
                TickSlowLane(hardware);
                continue;
            }

            long started = Stopwatch.GetTimestamp();
            UpdateRecursive(hardware);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed > SlowThreshold)
            {
                Log.Info($"Moving slow device to background lane: {hardware.HardwareType} '{hardware.Name}' ({elapsed.TotalMilliseconds:0} ms)");
                slowLane.Add(hardware);
            }
        }

        return [.. computer.Hardware.Select(Convert)];
    }

    public void Dispose()
    {
        try
        {
            Task.WaitAll([.. running.Values], TimeSpan.FromSeconds(5));
        }
        catch (Exception error)
        {
            Log.Error("Background hardware updates did not finish", error);
        }

        try
        {
            computer.Close();
        }
        catch (Exception error)
        {
            Log.Error("Computer.Close failed", error);
        }
    }

    private void TickSlowLane(IHardware hardware)
    {
        if (running.TryGetValue(hardware, out Task? current) && !current.IsCompleted)
        {
            return;
        }

        if (lastFinished.TryGetValue(hardware, out long finished) && Stopwatch.GetElapsedTime(finished) < SlowInterval)
        {
            return;
        }

        running[hardware] = Task.Run(() =>
        {
            try
            {
                UpdateRecursive(hardware);
            }
            catch (Exception error)
            {
                Log.Error($"Background update failed: {hardware.Name}", error);
            }
            finally
            {
                lastFinished[hardware] = Stopwatch.GetTimestamp();
            }
        });
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
