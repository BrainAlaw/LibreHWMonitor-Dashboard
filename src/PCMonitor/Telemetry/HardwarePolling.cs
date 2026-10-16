using System.Collections.Concurrent;
using System.Diagnostics;
using PCMonitor.Infra;

namespace PCMonitor.Telemetry;

internal interface IHardwareDevice
{
    string Name { get; }
    string Type { get; }
    IReadOnlyList<IHardwareDevice> Children { get; }
    void Update();
    IReadOnlyList<RawSensor> ReadSensors();
}

/// <summary>Each device owns its update and conversion; readers only see immutable cached trees.</summary>
internal sealed class HardwarePolling(Func<IReadOnlyList<IHardwareDevice>> getDevices, Action close) : IDisposable
{
    public HardwarePolling(IReadOnlyList<IHardwareDevice> devices, Action close) : this(() => devices, close) { }

    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(5);
    private readonly HashSet<IHardwareDevice> slowLane = [];
    private readonly Dictionary<IHardwareDevice, Task> running = [];
    private readonly ConcurrentDictionary<IHardwareDevice, long> lastFinished = new();
    private readonly ConcurrentDictionary<IHardwareDevice, RawHardware> snapshots = new();
    private readonly ConcurrentDictionary<IHardwareDevice, string> failures = new();
    private bool disposed;
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<string> Problems => failures.Values.Order(StringComparer.Ordinal).ToArray();
    public Task WhenClosed => closed.Task;

    public IReadOnlyList<RawHardware> Poll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        IReadOnlyList<IHardwareDevice> devices = getDevices();
        foreach (IHardwareDevice device in devices)
        {
            if (device.Type == "Storage" || slowLane.Contains(device))
            {
                if (running.TryGetValue(device, out Task? task) && !task.IsCompleted) continue;
                if (lastFinished.TryGetValue(device, out long finished) && Stopwatch.GetElapsedTime(finished) < SlowInterval) continue;
                running[device] = Task.Run(() =>
                {
                    try { snapshots[device] = Read(device); }
                    finally { lastFinished[device] = Stopwatch.GetTimestamp(); }
                });
            }
            else
            {
                long start = Stopwatch.GetTimestamp();
                snapshots[device] = Read(device);
                if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(1))
                {
                    slowLane.Add(device);
                    Log.Info($"Moving slow device to background lane: {device.Type} '{device.Name}'");
                }
            }
        }
        return devices.Select(d => snapshots.TryGetValue(d, out RawHardware? tree)
            ? tree : new RawHardware(d.Name, d.Type, [], [])).ToArray();
    }

    private RawHardware Read(IHardwareDevice device)
    {
        IReadOnlyList<RawSensor> sensors;
        try
        {
            device.Update();
            sensors = device.ReadSensors();
            failures.TryRemove(device, out _);
        }
        catch (Exception error)
        {
            string problem = $"Sensor update failed: {device.Name} ({device.Type}): {error.Message}";
            if (!failures.TryGetValue(device, out string? previous) || previous != problem)
                Log.Error(problem, error);
            failures[device] = problem;
            // Never publish a stale reading as a fresh measurement after an update failure.
            sensors = [];
        }
        return new RawHardware(device.Name, device.Type, sensors, device.Children.Select(Read).ToArray());
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Task updates = Task.WhenAll(running.Values);
        // Driver handles cannot be closed while a slow update is using them.
        if (updates.IsCompleted) Close();
        else _ = updates.ContinueWith(_ => Close(), TaskScheduler.Default);
    }

    private void Close()
    {
        try { close(); }
        catch (Exception error) { Log.Error("Hardware close failed", error); }
        finally { closed.TrySetResult(); }
    }
}
