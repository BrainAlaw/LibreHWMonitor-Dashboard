using PCMonitor.Settings;
using Xunit;

namespace PCMonitor.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pcmonitor-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UsesDefaultsWhenFileIsMissing()
    {
        SettingsStore store = new(FilePath, portOverride: "");

        Assert.Equal(AppSettings.DefaultPort, store.ListenPort);
        Assert.Equal(1000, store.Current.SamplingMs);
        Assert.True(store.Current.CloseToTray);
    }

    [Fact]
    public void FallsBackToDefaultsForCorruptJson()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "{ not json");

        SettingsStore store = new(FilePath, portOverride: "");

        Assert.Equal(AppSettings.DefaultPort, store.Current.Port);
    }

    [Fact]
    public void NormalizesOutOfRangeValues()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, """{ "Port": 99999, "SamplingMs": 5 }""");

        SettingsStore store = new(FilePath, portOverride: "");

        Assert.Equal(AppSettings.DefaultPort, store.Current.Port);
        Assert.Equal(AppSettings.MinSamplingMs, store.Current.SamplingMs);
    }

    [Fact]
    public void PersistsUpdatesAndReloadsThem()
    {
        SettingsStore store = new(FilePath, portOverride: "");
        store.Update(s =>
        {
            s.SamplingMs = 2500;
            s.StartMinimized = true;
        });

        SettingsStore reloaded = new(FilePath, portOverride: "");

        Assert.Equal(2500, reloaded.Current.SamplingMs);
        Assert.True(reloaded.Current.StartMinimized);
    }

    [Fact]
    public void PortOverrideAppliesToThisRunButIsNeverPersisted()
    {
        SettingsStore store = new(FilePath, portOverride: "9123");
        store.Update(s => s.StartMinimized = true);

        Assert.Equal(9123, store.ListenPort);
        Assert.Equal(AppSettings.DefaultPort, new SettingsStore(FilePath, portOverride: "").ListenPort);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    public void IgnoresInvalidPortOverrides(string value)
    {
        Assert.Equal(AppSettings.DefaultPort, new SettingsStore(FilePath, portOverride: value).ListenPort);
    }
}
