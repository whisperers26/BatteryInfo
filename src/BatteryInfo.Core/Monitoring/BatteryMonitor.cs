using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Monitoring;

/// <summary>Polls every provider on start, on an interval, on request, and shortly after device changes,
/// and merges the results into the <see cref="DeviceStore"/>. One slow or failing provider never holds up
/// the others.</summary>
public sealed class BatteryMonitor : IAsyncDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);

    private readonly IReadOnlyList<IBatteryProvider> _providers;
    private readonly DeviceStore _store;
    private readonly IDeviceChangeSource? _changes;
    private readonly TimeProvider _time;
    private readonly TimeSpan _providerTimeout;
    private readonly ITimer _pollTimer;
    private readonly ITimer _debounceTimer;
    private readonly object _gate = new();
    private readonly Dictionary<IBatteryProvider, Task> _running = [];
    private TimeSpan _pollInterval;
    private bool _started;
    private Task? _current;
    private Task? _next;

    public BatteryMonitor(IReadOnlyList<IBatteryProvider> providers, DeviceStore store, IDeviceChangeSource? changes,
        TimeProvider time, TimeSpan pollInterval, TimeSpan providerTimeout)
    {
        _providers = providers;
        _store = store;
        _changes = changes;
        _time = time;
        _pollInterval = pollInterval;
        _providerTimeout = providerTimeout;
        _pollTimer = time.CreateTimer(_ => _ = RefreshAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _debounceTimer = time.CreateTimer(_ => _ = RefreshAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        foreach (var provider in providers) provider.RefreshRequested += OnChange;
        if (changes is not null) changes.Changed += OnChange;
    }

    public event EventHandler<IReadOnlyList<DeviceState>>? SnapshotChanged;

    public DateTimeOffset? LastRefresh { get; private set; }

    public IReadOnlyList<DeviceState> Snapshot => _store.Snapshot();

    public TimeSpan PollInterval
    {
        get => _pollInterval;
        set
        {
            _pollInterval = value;
            if (_started) _pollTimer.Change(value, value);
        }
    }

    /// <summary>Starts the interval timer and returns the first poll.</summary>
    public Task Start()
    {
        _started = true;
        _pollTimer.Change(_pollInterval, _pollInterval);
        return RefreshAsync();
    }

    /// <summary>Polls all providers now. A call made while a poll is running queues exactly one more poll,
    /// which every such caller shares.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_current is null || _current.IsCompleted) return _current = Task.Run(PollAllAsync, CancellationToken.None);
            return _next ??= RunAfter(_current);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers) provider.RefreshRequested -= OnChange;
        if (_changes is not null) _changes.Changed -= OnChange;
        await _pollTimer.DisposeAsync();
        await _debounceTimer.DisposeAsync();
        _changes?.Dispose();
        foreach (var provider in _providers)
        {
            try
            {
                provider.Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"Disposing the {provider.Vendor} provider failed", e);
            }
        }
    }

    private void OnChange(object? sender, EventArgs e) => _debounceTimer.Change(Debounce, Timeout.InfiniteTimeSpan);

    private async Task RunAfter(Task current)
    {
        try
        {
            // Always yield. If the poll ended just after RefreshAsync checked it, continuing inline would clear
            // _next before RefreshAsync stores this task in it, leaving a finished task queued for good.
            await current.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        }
        catch (Exception e)
        {
            Log.Error("Previous poll failed", e);
        }

        Task poll;
        lock (_gate)
        {
            _next = null;
            _current = poll = Task.Run(PollAllAsync, CancellationToken.None);
        }
        await poll.ConfigureAwait(false);
    }

    private async Task PollAllAsync()
    {
        await Task.WhenAll(_providers.Select(PollOneAsync)).ConfigureAwait(false);
        LastRefresh = _time.GetUtcNow();
        var snapshot = _store.Snapshot();
        try
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
        catch (Exception e)
        {
            Log.Error("A snapshot listener failed", e);
        }
    }

    private async Task PollOneAsync(IBatteryProvider provider)
    {
        Task<IReadOnlyList<DeviceReading>> poll;
        var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            if (_running.TryGetValue(provider, out var previous) && !previous.IsCompleted)
            {
                _store.MarkVendorFailed(provider.Vendor, "The previous read has not finished yet.");
                cancellation.Dispose();
                return;
            }
            poll = Task.Run(() => provider.PollAsync(cancellation.Token), CancellationToken.None);
            _running[provider] = poll;
        }
        _ = poll.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);

        try
        {
            var readings = await poll.WaitAsync(_providerTimeout, _time).ConfigureAwait(false);
            _store.Apply(provider.Vendor, readings);
        }
        catch (TimeoutException)
        {
            cancellation.Cancel();
            Log.Warn($"{provider.Vendor} read timed out after {_providerTimeout.TotalSeconds:0.#} s");
            _store.MarkVendorFailed(provider.Vendor, "Timed out reading devices.");
        }
        catch (Exception e)
        {
            Log.Error($"{provider.Vendor} read failed", e);
            _store.MarkVendorFailed(provider.Vendor, e.Message);
        }
    }
}
