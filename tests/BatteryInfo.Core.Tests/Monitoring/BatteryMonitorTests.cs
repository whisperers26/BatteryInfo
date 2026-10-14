using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Tests.Fakes;

namespace BatteryInfo.Core.Tests.Monitoring;

public sealed class BatteryMonitorTests : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private readonly ManualTimeProvider _time = new();
    private readonly DeviceStore _store;
    private readonly FakeChanges _changes = new();
    private readonly List<BatteryMonitor> _monitors = [];

    public BatteryMonitorTests() => _store = new DeviceStore(_time);

    public async ValueTask DisposeAsync()
    {
        foreach (var monitor in _monitors) await monitor.DisposeAsync();
    }

    private sealed class FakeChanges : IDeviceChangeSource
    {
        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
        }
    }

    private sealed class FakeProvider(string vendor, Func<CancellationToken, Task<IReadOnlyList<DeviceReading>>>? poll = null) : IBatteryProvider
    {
        private int _polls;

        public string Vendor => vendor;

        public int Polls => Volatile.Read(ref _polls);

        public bool Disposed { get; private set; }

        public event EventHandler? RefreshRequested;

        public void RequestRefresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);

        public Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _polls);
            return poll?.Invoke(cancellationToken) ?? Task.FromResult<IReadOnlyList<DeviceReading>>(
            [
                new($"{vendor}:1", vendor, $"{vendor} device", DeviceKind.Mouse, DeviceStatus.Online,
                    [BatteryComponent.Single(50, ChargeState.Discharging)], new ManualTimeProvider().GetUtcNow(), "fake"),
            ]);
        }

        public void Dispose() => Disposed = true;
    }

    private BatteryMonitor Monitor(TimeProvider? time = null, TimeSpan? timeout = null, params IBatteryProvider[] providers)
    {
        var monitor = new BatteryMonitor(providers, _store, _changes, time ?? _time, Interval, timeout ?? TimeSpan.FromSeconds(15));
        _monitors.Add(monitor);
        return monitor;
    }

    private static Task<IReadOnlyList<DeviceState>> NextSnapshot(BatteryMonitor monitor)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<DeviceState>>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<IReadOnlyList<DeviceState>>? handler = null;
        handler = (_, snapshot) =>
        {
            monitor.SnapshotChanged -= handler;
            tcs.TrySetResult(snapshot);
        };
        monitor.SnapshotChanged += handler;
        return tcs.Task.WaitAsync(WaitLimit);
    }

    [Fact]
    public async Task Start_polls_every_provider_and_publishes_a_snapshot()
    {
        var logitech = new FakeProvider("Logitech");
        var rog = new FakeProvider("ROG");
        var monitor = Monitor(providers: [logitech, rog]);
        var snapshot = NextSnapshot(monitor);

        await monitor.Start().WaitAsync(WaitLimit);

        Assert.Equal(1, logitech.Polls);
        Assert.Equal(1, rog.Polls);
        Assert.Equal(2, (await snapshot).Count);
        Assert.Equal(_time.GetUtcNow(), monitor.LastRefresh);
    }

    [Fact]
    public async Task Polls_again_every_interval()
    {
        var provider = new FakeProvider("Betop");
        var monitor = Monitor(providers: provider);
        await monitor.Start().WaitAsync(WaitLimit);

        var next = NextSnapshot(monitor);
        _time.Advance(Interval);
        await next;

        Assert.Equal(2, provider.Polls);
    }

    [Fact]
    public async Task Failing_provider_is_marked_failed_and_others_still_apply()
    {
        var good = new FakeProvider("ROG");
        var bad = new FakeProvider("Razer", _ => throw new InvalidOperationException("boom"));
        _store.Apply("Razer", [new("razer:00D6", "Razer", "Basilisk", DeviceKind.Mouse, DeviceStatus.Online,
            [BatteryComponent.Single(80, ChargeState.Discharging)], _time.GetUtcNow(), "fake")]);
        var monitor = Monitor(providers: [good, bad]);

        await monitor.Start().WaitAsync(WaitLimit);

        var states = _store.Snapshot();
        Assert.Contains(states, s => s.Latest.DeviceId == "ROG:1" && s.Latest.Status == DeviceStatus.Online);
        var razer = Assert.Single(states, s => s.Latest.Vendor == "Razer");
        Assert.Equal(80, razer.DisplayComponents[0].Percent);
        Assert.Contains("boom", razer.Latest.Detail);
    }

    [Fact]
    public async Task Provider_exceeding_the_timeout_is_marked_failed()
    {
        var hung = new FakeProvider("HUAWEI", _ => new TaskCompletionSource<IReadOnlyList<DeviceReading>>().Task);
        var fine = new FakeProvider("Betop");
        var monitor = Monitor(TimeProvider.System, TimeSpan.FromMilliseconds(100), hung, fine);

        await monitor.Start().WaitAsync(WaitLimit);

        Assert.Single(_store.Snapshot(), s => s.Latest.Vendor == "Betop");
        Assert.Equal(1, hung.Polls);
    }

    [Fact]
    public async Task Hung_provider_is_skipped_while_its_previous_poll_still_runs()
    {
        var hung = new FakeProvider("HUAWEI", _ => new TaskCompletionSource<IReadOnlyList<DeviceReading>>().Task);
        var monitor = Monitor(TimeProvider.System, TimeSpan.FromMilliseconds(50), hung);

        await monitor.Start().WaitAsync(WaitLimit);
        await monitor.RefreshAsync().WaitAsync(WaitLimit);

        Assert.Equal(1, hung.Polls);
    }

    [Fact]
    public async Task Refresh_request_from_a_provider_polls_after_the_debounce()
    {
        var provider = new FakeProvider("HUAWEI");
        var monitor = Monitor(providers: provider);
        await monitor.Start().WaitAsync(WaitLimit);

        var next = NextSnapshot(monitor);
        provider.RequestRefresh();
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, provider.Polls);
        _time.Advance(TimeSpan.FromSeconds(1));
        await next;

        Assert.Equal(2, provider.Polls);
    }

    [Fact]
    public async Task Burst_of_device_changes_causes_one_poll()
    {
        var provider = new FakeProvider("Logitech");
        var monitor = Monitor(providers: provider);
        await monitor.Start().WaitAsync(WaitLimit);

        var next = NextSnapshot(monitor);
        _changes.Raise();
        _time.Advance(TimeSpan.FromSeconds(1));
        _changes.Raise();
        _time.Advance(TimeSpan.FromSeconds(1));
        _changes.Raise();
        _time.Advance(TimeSpan.FromSeconds(2));
        await next;
        await Task.Delay(100);

        Assert.Equal(2, provider.Polls);
    }

    [Fact]
    public async Task Refresh_during_a_running_poll_queues_one_more_poll()
    {
        var gate = new TaskCompletionSource();
        var calls = 0;
        var provider = new FakeProvider("ROG", async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1) await gate.Task;
            return [];
        });
        var monitor = Monitor(providers: provider);

        var first = monitor.Start();
        var second = monitor.RefreshAsync();
        var third = monitor.RefreshAsync();
        gate.SetResult();
        await Task.WhenAll(first, second, third).WaitAsync(WaitLimit);

        Assert.Same(second, third);
        Assert.Equal(2, provider.Polls);
    }

    [Fact]
    public async Task Dispose_disposes_providers()
    {
        var provider = new FakeProvider("Betop");
        var monitor = new BatteryMonitor([provider], _store, null, _time, Interval, TimeSpan.FromSeconds(15));

        await monitor.DisposeAsync();

        Assert.True(provider.Disposed);
    }
}
