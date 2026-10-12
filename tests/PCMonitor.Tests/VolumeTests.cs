using PCMonitor.Telemetry;
using Xunit;

namespace PCMonitor.Tests;

public sealed class VolumeMapperTests
{
    private const long Gb = 1024L * 1024 * 1024;

    // A real layout: NVMe drives as plain volumes C: and D:, four SATA drives combined into the dynamic-disk volume E:.
    private static readonly DiskRef[] Disks =
    [
        new(5, "Acer SSD FA200 4TB"),
        new(4, "Samsung SSD 970 EVO Plus 500GB"),
        new(2, "Crucial_CT1024MX200SSD1"),
        new(3, "HFS500G32TND-N1A2A"),
        new(1, "CT1000MX500SSD1"),
        new(0, "Crucial_CT960M500SSD1")
    ];

    private static readonly LogicalVolume[] Volumes =
    [
        new("E:", "SSD Data", "NTFS", 3223 * Gb, 2918 * Gb),
        new("C:", "OS", "NTFS", 465 * Gb, 189 * Gb),
        new("D:", "NVME Data", "NTFS", 3726 * Gb, 168 * Gb)
    ];

    private static readonly PartitionLink[] Links =
    [
        new(4, "C:"),
        new(5, "D:"),
        new(2, "E:"),
        new(1, "E:"),
        new(0, "E:"),
        new(3, "E:")
    ];

    [Fact]
    public void MapsEachVolumeToItsBackingDisksAndSortsByLetter()
    {
        IReadOnlyList<StorageVolume> result = VolumeMapper.Map(Disks, Links, Volumes);

        Assert.Equal(["C:", "D:", "E:"], result.Select(v => v.Id));
        Assert.Equal(["Samsung SSD 970 EVO Plus 500GB"], result[0].Disks);
        Assert.Equal(["Acer SSD FA200 4TB"], result[1].Disks);
    }

    [Fact]
    public void ReportsAllMemberDrivesOfASoftwareRaidVolume()
    {
        StorageVolume raid = VolumeMapper.Map(Disks, Links, Volumes).Single(v => v.Id == "E:");

        Assert.Equal(4, raid.Disks.Count);
        Assert.Contains("HFS500G32TND-N1A2A", raid.Disks);
        Assert.Contains("Crucial_CT960M500SSD1", raid.Disks);
        Assert.Equal(3223 * Gb, raid.TotalBytes);
        Assert.Equal(2918 * Gb, raid.FreeBytes);
    }

    [Fact]
    public void IgnoresEmptyVolumesAndUnknownDisks()
    {
        IReadOnlyList<StorageVolume> result = VolumeMapper.Map(
            Disks,
            [new PartitionLink(99, "F:"), new PartitionLink(5, "F:")],
            [new LogicalVolume("F:", "", "NTFS", 10 * Gb, 5 * Gb), new LogicalVolume("Z:", "", "", 0, 0)]);

        StorageVolume only = Assert.Single(result);
        Assert.Equal(["Acer SSD FA200 4TB"], only.Disks);
    }

    [Fact]
    public void DeduplicatesDisksThatHoldSeveralPartitionsOfTheSameVolume()
    {
        IReadOnlyList<StorageVolume> result = VolumeMapper.Map(
            Disks,
            [new PartitionLink(1, "E:"), new PartitionLink(1, "E:")],
            [new LogicalVolume("E:", "", "NTFS", Gb, Gb)]);

        Assert.Equal(["CT1000MX500SSD1"], result.Single().Disks);
    }
}

public sealed class CollectorVolumeTests
{
    private sealed class FakeVolumes(IReadOnlyList<StorageVolume> volumes) : IVolumeSource
    {
        public IReadOnlyList<StorageVolume> Read() => volumes;
    }

    [Fact]
    public async Task PublishesVolumesInTheSnapshot()
    {
        string path = Path.Combine(Path.GetTempPath(), "pcmonitor-tests-" + Guid.NewGuid().ToString("N"), "settings.json");
        PCMonitor.Settings.SettingsStore settings = new(path, portOverride: "");
        StorageVolume volume = new("E:", "Data", "NTFS", 100, 40, ["A", "B"]);
        using TelemetryCollector collector = new(
            settings,
            _ => new FakeSource(() => [new RawHardware("CPU", "Cpu", [new RawSensor("/amdcpu/0/load/0", "CPU Total", "Load", 0, 1f, null, null)], [])]),
            elevated: true,
            volumeSource: new FakeVolumes([volume]));
        collector.Start();

        TelemetrySnapshot? snapshot = null;
        for (int attempt = 0; attempt < 50 && snapshot?.Volumes.Count is null or 0; attempt++)
        {
            await collector.WaitForChangeAsync(collector.Latest?.Version ?? 0, TimeSpan.FromSeconds(2), CancellationToken.None);
            snapshot = collector.Latest?.Snapshot;
        }

        Assert.Equal(volume, Assert.Single(snapshot!.Volumes));
        Assert.Contains("\"volumes\":[{\"id\":\"E:\"", collector.Latest!.Json);
    }
}
