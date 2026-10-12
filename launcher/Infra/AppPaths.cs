namespace PCMonitor.Infra;

internal static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PCMonitor");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string UiOverrideDirectory => Path.Combine(DataDirectory, "ui");
    public static string WebViewDirectory => Path.Combine(DataDirectory, "WebView2");
}
