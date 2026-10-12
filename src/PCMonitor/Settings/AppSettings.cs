using System.Text.Json;
using PCMonitor.Infra;

namespace PCMonitor.Settings;

internal sealed class AppSettings
{
    public const int DefaultPort = 8096;
    public const int MinSamplingMs = 250;
    public const int MaxSamplingMs = 60_000;

    public int Port { get; set; } = DefaultPort;
    public int SamplingMs { get; set; } = 1000;
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool EnableCpu { get; set; } = true;
    public bool EnableGpu { get; set; } = true;
    public bool EnableMemory { get; set; } = true;
    public bool EnableMotherboard { get; set; } = true;
    public bool EnableStorage { get; set; } = true;
    public bool EnableNetwork { get; set; } = true;
    public bool EnableBattery { get; set; } = true;
    public bool EnablePsu { get; set; } = true;
    public bool EnableController { get; set; } = true;

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    public static bool IsValidSampling(int ms) => ms is >= MinSamplingMs and <= MaxSamplingMs;

    public void Normalize()
    {
        if (!IsValidPort(Port))
        {
            Port = DefaultPort;
        }

        SamplingMs = Math.Clamp(SamplingMs, MinSamplingMs, MaxSamplingMs);
    }
}

/// <summary>
/// Settings persisted as JSON. The <c>PORT</c> environment variable overrides the listen port
/// for the current run only; it is never written back to disk.
/// </summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly object syncRoot = new();
    private readonly string filePath;
    private readonly int? portOverride;
    private readonly AppSettings current;

    public SettingsStore(string? filePath = null, string? portOverride = null)
    {
        this.filePath = filePath ?? AppPaths.SettingsFile;
        current = Load(this.filePath);
        current.Normalize();

        if (int.TryParse(portOverride ?? Environment.GetEnvironmentVariable("PORT"), out int port) && AppSettings.IsValidPort(port))
        {
            this.portOverride = port;
        }
    }

    public AppSettings Current => current;

    /// <summary>The port to listen on: the environment override if present, otherwise the saved setting.</summary>
    public int ListenPort => portOverride ?? current.Port;

    public void Update(Action<AppSettings> mutate)
    {
        lock (syncRoot)
        {
            mutate(current);
            current.Normalize();
            Save();
        }
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception error)
        {
            Log.Error("Settings file is unreadable, using defaults", error);
        }

        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            string temp = filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(current, JsonOptions));
            File.Move(temp, filePath, overwrite: true);
        }
        catch (Exception error)
        {
            Log.Error("Settings could not be saved", error);
        }
    }
}
