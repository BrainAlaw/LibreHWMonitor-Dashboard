using System.Globalization;
using LibreHardwareMonitor.Hardware;

namespace PCMonitor.Telemetry;

internal static class SensorFormatting
{
    public static string GroupName(SensorType type) => type switch
    {
        SensorType.Clock => "Clocks",
        SensorType.Control => "Controls",
        SensorType.Data => "Data",
        SensorType.Factor => "Factors",
        SensorType.Fan => "Fans",
        SensorType.Flow => "Flows",
        SensorType.Frequency => "Frequencies",
        SensorType.Level => "Levels",
        SensorType.Load => "Load",
        SensorType.Power => "Powers",
        SensorType.SmallData => "Data",
        SensorType.Temperature => "Temperatures",
        SensorType.Throughput => "Throughput",
        SensorType.TimeSpan => "Times",
        SensorType.Timing => "Timings",
        SensorType.Voltage => "Voltages",
        _ => type.ToString()
    };

    public static (string Unit, int Decimals) UnitAndPrecision(SensorType type) => type switch
    {
        SensorType.Clock => ("MHz", 1),
        SensorType.Control => ("%", 1),
        SensorType.Data => ("GB", 1),
        SensorType.Factor => (string.Empty, 3),
        SensorType.Timing => ("ns", 3),
        SensorType.Fan => ("RPM", 0),
        SensorType.Flow => ("L/h", 1),
        SensorType.Frequency => ("Hz", 0),
        SensorType.Level => ("%", 1),
        SensorType.Load => ("%", 1),
        SensorType.Power => ("W", 1),
        SensorType.SmallData => ("MB", 0),
        SensorType.Temperature => ("°C", 1),
        SensorType.Throughput => ("KB/s", 1),
        SensorType.TimeSpan => ("s", 1),
        SensorType.Voltage => ("V", 3),
        _ => (string.Empty, 2)
    };

    public static bool IsFinite(float? value) =>
        value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value);

    public static string FormatValue(float? value, SensorType type)
    {
        if (!IsFinite(value))
        {
            return string.Empty;
        }

        (string unit, int decimals) = UnitAndPrecision(type);
        string format = decimals <= 0 ? "0" : $"0.{new string('#', decimals)}";
        string number = value!.Value.ToString(format, CultureInfo.InvariantCulture);
        return unit.Length == 0 ? number : $"{number} {unit}";
    }

    public static double? Numeric(float? value, SensorType type)
    {
        if (!IsFinite(value))
        {
            return null;
        }

        return Math.Round(value!.Value, UnitAndPrecision(type).Decimals + 1, MidpointRounding.AwayFromZero);
    }

    public static string ImageForHardware(HardwareType type) => type switch
    {
        HardwareType.Cpu => "images_cpu.png",
        HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia => "images_gpu.png",
        HardwareType.Memory => "images_ram.png",
        HardwareType.Motherboard => "images_mainboard.png",
        HardwareType.Network => "images_nic.png",
        HardwareType.Storage => "images_hdd.png",
        _ => "images_chip.png"
    };

    public static string ImageForSensorType(SensorType type) => type switch
    {
        SensorType.Clock => "images_clock.png",
        SensorType.Control => "images_control.png",
        SensorType.Data or SensorType.SmallData => "images_hdd.png",
        SensorType.Fan => "images_fan.png",
        SensorType.Load => "images_load.png",
        SensorType.Power => "images_power.png",
        SensorType.Temperature => "images_temperature.png",
        SensorType.Throughput => "images_throughput.png",
        SensorType.Voltage => "images_voltage.png",
        _ => "images_sensor.png"
    };
}
