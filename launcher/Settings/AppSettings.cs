using System.Text.Json;
using PCMonitor.Infra;

namespace PCMonitor.Settings;

internal sealed class AppSettings
{
    public const int DefaultPort = 8096;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public int Port { get; set; } = DefaultPort;
    public int SamplingMs { get; set; } = 1000;
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public string? ExternalSource { get; set; }
    public bool EnableCpu { get; set; } = true;
    public bool EnableGpu { get; set; } = true;
    public bool EnableMemory { get; set; } = true;
    public bool EnableMotherboard { get; set; } = true;
    public bool EnableStorage { get; set; } = true;
    public bool EnableNetwork { get; set; } = true;
    public bool EnableBattery { get; set; } = true;
    public bool EnablePsu { get; set; } = true;
    public bool EnableController { get; set; } = true;

    public void Normalize()
    {
        if (Port is < 1 or > 65535)
        {
            Port = DefaultPort;
        }

        SamplingMs = Math.Clamp(SamplingMs, 250, 60_000);

        if (!Uri.TryCreate(ExternalSource, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ExternalSource = null;
        }
    }
}

internal sealed class SettingsStore
{
    private readonly object syncRoot = new();
    private readonly AppSettings current;
    private readonly int persistedPort;
    private readonly string? persistedSource;

    public SettingsStore()
    {
        current = Load();
        current.Normalize();
        persistedPort = current.Port;
        persistedSource = current.ExternalSource;
        ApplyEnvironmentOverrides(current);
        current.Normalize();
    }

    public AppSettings Current => current;

    public void Update(Action<AppSettings> mutate)
    {
        lock (syncRoot)
        {
            // Environment overrides apply to this run only, so they are never written back to disk.
            bool portOverridden = current.Port != persistedPort;
            bool sourceOverridden = current.ExternalSource != persistedSource;
            int runtimePort = current.Port;
            string? runtimeSource = current.ExternalSource;

            mutate(current);
            current.Normalize();

            int requestedPort = current.Port;
            string? requestedSource = current.ExternalSource;
            if (portOverridden && requestedPort == runtimePort)
            {
                current.Port = persistedPort;
            }

            if (sourceOverridden && requestedSource == runtimeSource)
            {
                current.ExternalSource = persistedSource;
            }

            Save(current);
            current.Port = requestedPort;
            current.ExternalSource = requestedSource;
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                string json = File.ReadAllText(AppPaths.SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, AppSettings.JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception error)
        {
            Log.Error("Settings file is unreadable, using defaults", error);
        }

        return new AppSettings();
    }

    private static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            string temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, AppSettings.JsonOptions));
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception error)
        {
            Log.Error("Settings could not be saved", error);
        }
    }

    private static void ApplyEnvironmentOverrides(AppSettings settings)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out int port))
        {
            settings.Port = port;
        }

        string? source = Environment.GetEnvironmentVariable("PCMONITOR_TELEMETRY_SOURCE");
        if (string.IsNullOrWhiteSpace(source))
        {
            source = Environment.GetEnvironmentVariable("LIBREHW_SOURCE");
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            settings.ExternalSource = source;
        }
    }
}
