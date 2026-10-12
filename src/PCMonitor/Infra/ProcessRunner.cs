using System.Diagnostics;

namespace PCMonitor.Infra;

internal static class ProcessRunner
{
    private const int TimeoutMs = 10_000;

    /// <summary>Runs a console tool without a window and returns its exit code and combined output.</summary>
    public static (int ExitCode, string Output) Run(string fileName, params string[] arguments)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeoutMs))
            {
                process.Kill();
                return (-1, "Timed out");
            }

            if (process.ExitCode != 0)
            {
                Log.Info($"{fileName} {string.Join(' ', arguments)} -> {process.ExitCode}: {output.Trim()}");
            }

            return (process.ExitCode, output);
        }
        catch (Exception error)
        {
            Log.Error($"{fileName} failed", error);
            return (-1, error.Message);
        }
    }
}
