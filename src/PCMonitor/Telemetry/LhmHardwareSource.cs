using LibreHardwareMonitor.Hardware;
using PCMonitor.Infra;
using PCMonitor.Settings;

namespace PCMonitor.Telemetry;

/// <summary>
/// Adapter over LibreHardwareMonitor's <see cref="Computer"/>.
/// Some devices (every storage drive: about 2 s per update) are far slower than the sampling interval.
/// Those run in a background "slow lane", one task per device and at most every five seconds,
/// so they never hold up the fast sensors. Their last known values are served in between.
/// </summary>
internal sealed class LhmHardwareSource : IHardwareSource
{
    private readonly Computer computer;
    private readonly HardwarePolling polling;
    private readonly Dictionary<IHardware, Device> devices = [];

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
        try
        {
            computer.Open();
            polling = new HardwarePolling(() => computer.Hardware.Select(GetDevice).ToArray(), computer.Close);
        }
        catch
        {
            try { computer.Close(); }
            catch (Exception error) { Log.Error("Hardware cleanup after initialization failure failed", error); }
            throw;
        }
        Log.Info($"Hardware opened: {computer.Hardware.Count} devices");
    }

    public int HardwareCount => computer.Hardware.Count;
    public static string? LibraryVersion => typeof(Computer).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;

    public IReadOnlyList<RawHardware> Poll() => polling.Poll();
    public IReadOnlyList<string> Problems => polling.Problems;
    public Task WhenClosed => polling.WhenClosed;

    public DriverStatus Driver
    {
        get
        {
            try
            {
                return new DriverStatus(LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled,
                    LibreHardwareMonitor.PawnIo.PawnIo.Version?.ToString());
            }
            catch (Exception error)
            {
                return new DriverStatus(false, null, error.GetBaseException().Message);
            }
        }
    }

    public void Dispose() => polling.Dispose();

    private IHardwareDevice GetDevice(IHardware hardware)
    {
        if (!devices.TryGetValue(hardware, out Device? device))
        {
            device = new Device(hardware);
            devices.Add(hardware, device);
        }
        return device;
    }

    private sealed class Device(IHardware hardware) : IHardwareDevice
    {
        private readonly Dictionary<IHardware, Device> children = [];
        public string Name => hardware.Name;
        public string Type => hardware.HardwareType.ToString();
        public IReadOnlyList<IHardwareDevice> Children => hardware.SubHardware.Select(h =>
        {
            if (!children.TryGetValue(h, out Device? device))
            {
                device = new Device(h);
                children.Add(h, device);
            }
            return (IHardwareDevice)device;
        }).ToArray();
        public void Update() => hardware.Update();
        public IReadOnlyList<RawSensor> ReadSensors() => hardware.Sensors.Select(s => new RawSensor(
            s.Identifier.ToString(), s.Name, s.SensorType.ToString(), s.Index, s.Value, s.Min, s.Max)).ToArray();
    }
}
