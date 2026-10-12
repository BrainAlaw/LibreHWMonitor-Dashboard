namespace PCMonitor.Infra;

internal static class Log
{
    private const int RetainDays = 7;
    private static readonly object SyncRoot = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? error = null) =>
        Write("ERROR", error is null ? message : $"{message}: {error}");

    public static void Prune()
    {
        try
        {
            if (!Directory.Exists(AppPaths.LogDirectory))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(AppPaths.LogDirectory, "app-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-RetainDays))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Log pruning is best effort.
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                string file = Path.Combine(AppPaths.LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
