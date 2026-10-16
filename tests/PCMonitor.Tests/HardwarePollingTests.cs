using PCMonitor.Telemetry;
using Xunit;

namespace PCMonitor.Tests;

public sealed class HardwarePollingTests
{
    private sealed class Device(string name, string type = "Cpu") : IHardwareDevice
    {
        public string Name => name;
        public string Type => type;
        public IReadOnlyList<IHardwareDevice> Children { get; init; } = [];
        public Action OnUpdate { get; set; } = () => { };
        public void Update() => OnUpdate();
        public IReadOnlyList<RawSensor> ReadSensors() => [new RawSensor($"/{name}/temperature/0", "Temperature", "Temperature", 0, 50, null, null)];
    }

    [Fact]
    public void FollowsChangesToTheHardwareInventory()
    {
        IReadOnlyList<IHardwareDevice> devices = [new Device("CPU")];
        using var polling = new HardwarePolling(() => devices, () => { });
        Assert.Single(polling.Poll());
        devices = [devices[0], new Device("GPU", "GpuNvidia")];
        Assert.Equal(2, polling.Poll().Count);
        devices = [devices[1]];
        Assert.Equal("GPU", Assert.Single(polling.Poll()).Name);
    }

    [Fact]
    public void FailedDeviceDoesNotEraseHealthyDevicesAndRecoversOnNextPoll()
    {
        var cpu = new Device("CPU");
        var gpu = new Device("GPU", "GpuNvidia");
        using var polling = new HardwarePolling([cpu, gpu], () => { });
        Assert.All(polling.Poll(), h => Assert.Single(h.Sensors));
        cpu.OnUpdate = () => throw new InvalidOperationException("driver access failed");
        var failed = polling.Poll();
        Assert.Empty(failed[0].Sensors);
        Assert.Single(failed[1].Sensors);
        Assert.Contains("driver access failed", Assert.Single(polling.Problems));
        cpu.OnUpdate = () => { };
        Assert.All(polling.Poll(), h => Assert.Single(h.Sensors));
        Assert.Empty(polling.Problems);
    }

    [Fact]
    public void FailedChildDoesNotPreventSiblingUpdates()
    {
        var failed = new Device("Bad", "SuperIO") { OnUpdate = () => throw new InvalidOperationException("blocked") };
        var board = new Device("Board", "Motherboard") { Children = [failed, new Device("Good", "EmbeddedController")] };
        using var polling = new HardwarePolling([board], () => { });
        var tree = Assert.Single(polling.Poll());
        Assert.Empty(tree.Children[0].Sensors);
        Assert.Single(tree.Children[1].Sensors);
        Assert.Single(polling.Problems);
    }

    [Fact]
    public async Task SlowDeviceDoesNotBlockPollingOrCloseWhileUpdateIsRunning()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new Device("Disk", "Storage")
        {
            OnUpdate = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); }
        };
        using var polling = new HardwarePolling([storage, new Device("CPU")], () => closed.SetResult());
        try
        {
            var first = polling.Poll();
            Assert.Single(first[1].Sensors);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Empty(polling.Poll()[0].Sensors);
            polling.Dispose();
            Assert.False(closed.Task.IsCompleted);
        }
        finally { release.Set(); }
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
