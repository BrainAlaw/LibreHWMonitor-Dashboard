using System.Management;
using System.Text.RegularExpressions;
using PCMonitor.Infra;

namespace PCMonitor.Telemetry;

/// <summary>A logical volume (drive letter) and the physical drives that back it.</summary>
internal sealed record StorageVolume(
    string Id,
    string Label,
    string FileSystem,
    long TotalBytes,
    long FreeBytes,
    IReadOnlyList<string> Disks);

internal sealed record DiskRef(int Index, string Model);

/// <summary>Windows reports one link per partition that carries part of a logical disk.</summary>
internal sealed record PartitionLink(int DiskIndex, string VolumeId);

internal sealed record LogicalVolume(string Id, string Label, string FileSystem, long TotalBytes, long FreeBytes);

internal interface IVolumeSource
{
    IReadOnlyList<StorageVolume> Read();
}

/// <summary>
/// Joins volumes to physical drives. A volume backed by several drives is a software RAID (spanned, striped,
/// mirrored or RAID 5 dynamic disk), which per-drive sensors cannot describe on their own.
/// </summary>
internal static class VolumeMapper
{
    public static IReadOnlyList<StorageVolume> Map(
        IEnumerable<DiskRef> disks,
        IEnumerable<PartitionLink> links,
        IEnumerable<LogicalVolume> volumes)
    {
        Dictionary<int, string> modelByIndex = disks.GroupBy(d => d.Index).ToDictionary(g => g.Key, g => g.First().Model);
        List<PartitionLink> linkList = [.. links];

        return [.. volumes
            .Where(v => v.TotalBytes > 0)
            .OrderBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .Select(v => new StorageVolume(
                v.Id,
                v.Label,
                v.FileSystem,
                v.TotalBytes,
                v.FreeBytes,
                [.. linkList
                    .Where(l => l.VolumeId.Equals(v.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(l => modelByIndex.TryGetValue(l.DiskIndex, out string? model) ? model : null)
                    .OfType<string>()
                    .Distinct()
                    .Order(StringComparer.OrdinalIgnoreCase)]))];
    }
}

/// <summary>Reads volumes and their disk mapping from WMI.</summary>
internal sealed partial class WmiVolumeSource : IVolumeSource
{
    private const int FixedDisk = 3;

    public IReadOnlyList<StorageVolume> Read()
    {
        try
        {
            List<DiskRef> disks = [.. Query("SELECT Index, Model FROM Win32_DiskDrive")
                .Select(o => new DiskRef(Convert.ToInt32(o["Index"]), o["Model"]?.ToString()?.Trim() ?? string.Empty))];

            List<PartitionLink> links = [.. Query("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition")
                .Select(ParseLink)
                .OfType<PartitionLink>()];

            List<LogicalVolume> volumes = [.. Query($"SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = {FixedDisk}")
                .Select(o => new LogicalVolume(
                    o["DeviceID"]?.ToString() ?? string.Empty,
                    o["VolumeName"]?.ToString() ?? string.Empty,
                    o["FileSystem"]?.ToString() ?? string.Empty,
                    ToLong(o["Size"]),
                    ToLong(o["FreeSpace"])))];

            return VolumeMapper.Map(disks, links, volumes);
        }
        catch (Exception error)
        {
            Log.Error("Volume query failed", error);
            return [];
        }
    }

    internal static PartitionLink? ParseLink(ManagementBaseObject link)
    {
        Match disk = DiskNumber().Match(link["Antecedent"]?.ToString() ?? string.Empty);
        Match volume = VolumeLetter().Match(link["Dependent"]?.ToString() ?? string.Empty);
        return disk.Success && volume.Success
            ? new PartitionLink(int.Parse(disk.Groups[1].Value), volume.Groups[1].Value)
            : null;
    }

    private static List<ManagementBaseObject> Query(string query)
    {
        using ManagementObjectSearcher searcher = new(query);
        using ManagementObjectCollection results = searcher.Get();
        return [.. results.Cast<ManagementBaseObject>()];
    }

    private static long ToLong(object? value) => value is null ? 0 : Convert.ToInt64(value);

    [GeneratedRegex(@"Disk #(\d+)")]
    private static partial Regex DiskNumber();

    [GeneratedRegex(@"DeviceID\s*=\s*""([A-Za-z]:)""")]
    private static partial Regex VolumeLetter();
}
