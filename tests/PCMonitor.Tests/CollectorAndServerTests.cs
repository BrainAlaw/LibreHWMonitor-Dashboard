using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PCMonitor.Server;
using PCMonitor.Settings;
using PCMonitor.Telemetry;
using Xunit;

namespace PCMonitor.Tests;

internal sealed class FakeSource(Func<IReadOnlyList<RawHardware>> poll) : IHardwareSource
{
    public int HardwareCount => 1;
    public bool Disposed { get; private set; }
    public Task WhenClosed { get; init; } = Task.CompletedTask;
    public IReadOnlyList<RawHardware> Poll() => poll();
    public void Dispose() => Disposed = true;
}

public sealed class TelemetryCollectorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pcmonitor-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private SettingsStore Settings()
    {
        SettingsStore settings = new(Path.Combine(directory, "settings.json"), portOverride: "");
        settings.Current.EnableMotherboard = false;
        return settings;
    }

    private static RawHardware Cpu(float load) =>
        new("CPU", "Cpu", [new RawSensor("/amdcpu/0/load/0", "CPU Total", "Load", 0, load, null, null),
            new RawSensor("/amdcpu/0/temperature/0", "Core (Tctl/Tdie)", "Temperature", 0, 50, null, null)], []);

    [Fact]
    public async Task PublishesSnapshotsAndHealth()
    {
        using TelemetryCollector collector = new(Settings(), _ => new FakeSource(() => [Cpu(42)]), elevated: true);
        collector.Start();

        await collector.WaitForChangeAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.NotNull(collector.Latest);
        Assert.Equal(42, collector.Latest!.Snapshot.Sensors.First(s => s.Type == "Load").Value);
        Assert.Equal("ok", collector.Health.Status);
        Assert.Equal(2, collector.Health.SensorCount);
        Assert.Equal(2, collector.Health.AvailableSensorCount);
    }

    [Fact]
    public async Task ReportsDegradedWhenNotElevated()
    {
        using TelemetryCollector collector = new(Settings(), _ => new FakeSource(() => [Cpu(1)]), elevated: false);
        collector.Start();

        await collector.WaitForChangeAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal("degraded", collector.Health.Status);
        Assert.Contains("Administrator", collector.Health.LastError);
    }

    [Fact]
    public async Task RecoversAfterTheSourceFails()
    {
        int attempts = 0;
        using TelemetryCollector collector = new(
            Settings(),
            _ => ++attempts == 1 ? throw new InvalidOperationException("driver blocked") : new FakeSource(() => [Cpu(7)]),
            elevated: true,
            retryDelay: TimeSpan.FromMilliseconds(50));
        collector.Start();

        await collector.WaitForChangeAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal("error", collector.Health.Status);
        Assert.Equal("driver blocked", collector.Health.LastError);

        await collector.WaitForChangeAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal("ok", collector.Health.Status);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ResetRecreatesTheSource()
    {
        List<FakeSource> created = [];
        using TelemetryCollector collector = new(Settings(), _ =>
        {
            FakeSource source = new(() => [Cpu(1)]);
            created.Add(source);
            return source;
        }, elevated: true);
        collector.Start();
        await collector.WaitForChangeAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);

        collector.Reset();
        await collector.WaitForChangeAsync(collector.Latest!.Version, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(2, created.Count);
        Assert.True(created[0].Disposed);
    }

    [Fact]
    public async Task ResetWaitsForOldHardwareToCloseBeforeOpeningNewHardware()
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int created = 0;
        FakeSource first = new(() => [Cpu(1)]) { WhenClosed = closed.Task };
        using TelemetryCollector collector = new(Settings(), _ =>
            Interlocked.Increment(ref created) == 1 ? first : new FakeSource(() => [Cpu(2)]), true);
        collector.Start();
        await collector.WaitForChangeAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);
        long beforeReset = collector.Latest!.Version;
        collector.Reset();
        try
        {
            Assert.True(SpinWait.SpinUntil(() => first.Disposed, TimeSpan.FromSeconds(5)));
            Assert.Equal(1, Volatile.Read(ref created));
            closed.SetResult();
            await collector.WaitForChangeAsync(beforeReset, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.Equal(2, Volatile.Read(ref created));
        }
        finally { closed.TrySetResult(); }
    }
}

public sealed class DashboardHostTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pcmonitor-tests-" + Guid.NewGuid().ToString("N"));
    private TelemetryCollector collector = null!;
    private DashboardHost host = null!;
    private HttpClient client = null!;
    private bool autostart;
    private bool firewallOpen;

    public async Task InitializeAsync()
    {
        SettingsStore settings = new(Path.Combine(directory, "settings.json"), portOverride: "18431");
        collector = new TelemetryCollector(
            settings,
            _ => new FakeSource(() => [new RawHardware("CPU", "Cpu", [new RawSensor("/amdcpu/0/load/0", "CPU Total", "Load", 0, 55f, null, null)], [])]),
            elevated: true);
        collector.Start();
        await collector.WaitForChangeAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);

        host = await DashboardHost.StartAsync(settings, collector, new SystemActions(
            enable => { autostart = enable; return Task.FromResult(true); },
            () => autostart,
            enable => { firewallOpen = enable; return Task.FromResult(true); }));
        client = new HttpClient { BaseAddress = new Uri(host.Url) };
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await host.DisposeAsync();
        collector.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ServesTheEmbeddedDashboard()
    {
        string page = await client.GetStringAsync("/");

        Assert.Contains("<title>PC Monitor</title>", page);
    }

    [Fact]
    public async Task ServesTheSnapshotAsCamelCaseJson()
    {
        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/snapshot"));

        JsonElement sensor = json.RootElement.GetProperty("sensors")[0];
        Assert.Equal("CPU Total", sensor.GetProperty("name").GetString());
        Assert.Equal(55, sensor.GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task ReportsHealth()
    {
        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/health"));

        Assert.Equal("degraded", json.RootElement.GetProperty("status").GetString());
        Assert.Contains("CPU temperature unavailable", json.RootElement.GetProperty("lastError").GetString());
        Assert.Equal(host.Port, json.RootElement.GetProperty("port").GetInt32());
    }

    [Fact]
    public async Task DownloadsLocalDiagnosticsWithoutSettingsSecrets()
    {
        using HttpResponseMessage response = await client.GetAsync("/api/diagnostics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument report = JsonDocument.Parse(body);
        Assert.True(report.RootElement.TryGetProperty("hardwareLibraryVersion", out _));
        Assert.NotEmpty(report.RootElement.GetProperty("health").GetProperty("problems").EnumerateArray());
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lanUrls", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsForeignHostHeaders()
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/health");
        request.Headers.Host = "evil.example";

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RejectsSettingsWritesWithoutSameOrigin()
    {
        using HttpResponseMessage response = await client.PutAsync("/api/settings",
            new StringContent("""{"samplingMs":2000}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AcceptsSameOriginSettingsAndTogglesAutostart()
    {
        using HttpRequestMessage request = new(HttpMethod.Put, "/api/settings")
        {
            Content = new StringContent("""{"samplingMs":2000,"autostart":true}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", $"http://127.0.0.1:{host.Port}");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(autostart);
        using JsonDocument settings = JsonDocument.Parse(await client.GetStringAsync("/api/settings"));
        Assert.Equal(2000, settings.RootElement.GetProperty("samplingMs").GetInt32());
        Assert.True(settings.RootElement.GetProperty("autostart").GetBoolean());
    }

    [Fact]
    public async Task EnablingLanGeneratesATokenOpensTheFirewallAndRequiresARestart()
    {
        using HttpRequestMessage request = new(HttpMethod.Put, "/api/settings")
        {
            Content = new StringContent("""{"allowLan":true}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", $"http://127.0.0.1:{host.Port}");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("restartRequired").GetBoolean());
        Assert.True(body.RootElement.GetProperty("firewallOk").GetBoolean());
        Assert.True(firewallOpen);
        using JsonDocument view = JsonDocument.Parse(await client.GetStringAsync("/api/settings"));
        Assert.True(view.RootElement.GetProperty("allowLan").GetBoolean());
        Assert.Equal(JsonValueKind.Array, view.RootElement.GetProperty("lanUrls").ValueKind);
    }

    [Fact]
    public async Task RejectsOutOfRangeSettings()
    {
        using HttpRequestMessage request = new(HttpMethod.Put, "/api/settings")
        {
            Content = JsonContent.Create(new { port = 70000 })
        };
        request.Headers.Add("Origin", $"http://127.0.0.1:{host.Port}");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StreamsASnapshotEventFirst()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using HttpResponseMessage response = await client.GetAsync("/api/stream", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using StreamReader reader = new(await response.Content.ReadAsStreamAsync(timeout.Token));

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("event: snapshot", await reader.ReadLineAsync(timeout.Token));
        Assert.StartsWith("data: {", await reader.ReadLineAsync(timeout.Token));
    }

    [Fact]
    public async Task FallsBackToTheNextPortWhenBusy()
    {
        SettingsStore settings = new(Path.Combine(directory, "other.json"), portOverride: host.Port.ToString());

        await using DashboardHost second = await DashboardHost.StartAsync(
            settings, collector, new SystemActions(_ => Task.FromResult(true), () => false, _ => Task.FromResult(true)));

        Assert.Equal(host.Port + 1, second.Port);
    }
}
