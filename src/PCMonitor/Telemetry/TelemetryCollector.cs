using System.Diagnostics;
using System.Text.Json;
using PCMonitor.Infra;
using PCMonitor.Settings;

namespace PCMonitor.Telemetry;

/// <summary>
/// Polls hardware on one background loop and publishes immutable snapshots.
/// Consumers (HTTP, tray) only read the latest snapshot or await the next one; none touch hardware.
/// </summary>
internal sealed class TelemetryCollector : IDisposable
{
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan VolumeInterval = TimeSpan.FromSeconds(10);

    private readonly SettingsStore settings;
    private readonly Func<AppSettings, IHardwareSource> sourceFactory;
    private readonly TimeSpan retryDelay;
    private readonly IVolumeSource? volumeSource;
    private readonly bool elevated;
    private readonly CancellationTokenSource cancellation = new();
    private readonly object feedLock = new();
    private TaskCompletionSource changed = NewSignal();
    private volatile bool resetRequested;
    private PublishedSnapshot? latest;
    private TelemetryHealth health;
    private long version;
    private IReadOnlyList<StorageVolume> volumes = [];
    private long volumesReadAt;
    private Task? volumeRefresh;
    private Task? loop;

    public TelemetryCollector(
        SettingsStore settings,
        Func<AppSettings, IHardwareSource> sourceFactory,
        bool elevated,
        TimeSpan? retryDelay = null,
        IVolumeSource? volumeSource = null)
    {
        this.volumeSource = volumeSource;
        this.settings = settings;
        this.sourceFactory = sourceFactory;
        this.elevated = elevated;
        this.retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
        health = new TelemetryHealth("starting", elevated, 0, 0, null, null);
    }

    public PublishedSnapshot? Latest => Volatile.Read(ref latest);
    public TelemetryHealth Health => Volatile.Read(ref health);

    public void Start() => loop = Task.Run(() => RunAsync(cancellation.Token));

    public void Reset() => resetRequested = true;

    /// <summary>Completes when a snapshot newer than <paramref name="afterVersion"/> or a health change exists.</summary>
    public async Task WaitForChangeAsync(long afterVersion, TimeSpan timeout, CancellationToken token)
    {
        Task signal;
        lock (feedLock)
        {
            if (Interlocked.Read(ref version) > afterVersion)
            {
                return;
            }

            signal = changed.Task;
        }

        try
        {
            await signal.WaitAsync(timeout, token);
        }
        catch (TimeoutException)
        {
            // Timeout is the normal "nothing new" outcome; the caller re-checks health.
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Shutdown is best effort.
        }

        cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        IHardwareSource? source = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan delay = TimeSpan.FromMilliseconds(settings.Current.SamplingMs);
                try
                {
                    if (resetRequested)
                    {
                        resetRequested = false;
                        await CloseSourceAsync(source, token);
                        source = null;
                        Log.Info("Collector reset requested");
                    }

                    source ??= sourceFactory(settings.Current);
                    Publish(source);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    Log.Error("Telemetry sampling failed", error);
                    await CloseSourceAsync(source, token);
                    source = null;
                    SetHealth(new TelemetryHealth("error", elevated, 0, 0, error.Message, Health.LastUpdate));
                    delay = retryDelay;
                }

                await Task.Delay(delay, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            DisposeSource(ref source);
        }
    }

    private void Publish(IHardwareSource source)
    {
        IReadOnlyList<RawHardware> devices = source.Poll();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TelemetrySnapshot snapshot = SnapshotBuilder.Build(now, devices, CurrentVolumes());
        int sensorCount = snapshot.Sensors.Count;

        DriverStatus? driver = source.Driver;
        IReadOnlyList<string> problems = TelemetryDiagnostics.Problems(snapshot, settings.Current, elevated, driver, source.Problems);
        string? problem = problems.Count > 0 ? string.Join("; ", problems) : null;

        lock (feedLock)
        {
            long next = Interlocked.Increment(ref version);
            Volatile.Write(ref latest, new PublishedSnapshot(next, snapshot, JsonSerializer.Serialize(snapshot, SnapshotJson)));
            Volatile.Write(ref health, new TelemetryHealth(
                problem is null ? "ok" : "degraded", elevated, source.HardwareCount, sensorCount, problem, now,
                problems, driver, snapshot.Sensors.Count(s => s.Value.HasValue)));
            Signal();
        }
    }

    // WMI is slow compared with the sampling loop and volumes change rarely, so they refresh in the background.
    private IReadOnlyList<StorageVolume> CurrentVolumes()
    {
        bool idle = volumeRefresh is null || volumeRefresh.IsCompleted;
        bool due = volumesReadAt == 0 || Stopwatch.GetElapsedTime(volumesReadAt) > VolumeInterval;
        if (volumeSource is not null && idle && due)
        {
            volumeRefresh = Task.Run(() =>
            {
                try
                {
                    Volatile.Write(ref volumes, volumeSource.Read());
                }
                catch (Exception error)
                {
                    Log.Error("Volume refresh failed", error);
                }
                finally
                {
                    Interlocked.Exchange(ref volumesReadAt, Stopwatch.GetTimestamp());
                }
            });
        }

        return Volatile.Read(ref volumes);
    }

    private void SetHealth(TelemetryHealth value)
    {
        lock (feedLock)
        {
            Volatile.Write(ref health, value);
            Signal();
        }
    }

    // Callers hold feedLock.
    private void Signal()
    {
        TaskCompletionSource previous = changed;
        changed = NewSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void DisposeSource(ref IHardwareSource? source)
    {
        source?.Dispose();
        source = null;
    }

    private static async Task CloseSourceAsync(IHardwareSource? source, CancellationToken token)
    {
        if (source is null) return;
        source.Dispose();
        // Reopening the library before old updates release driver handles can corrupt the new session.
        await source.WhenClosed.WaitAsync(token);
    }
}
