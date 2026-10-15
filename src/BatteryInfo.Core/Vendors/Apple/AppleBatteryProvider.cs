using System.Globalization;
using BatteryInfo.Core.Bluetooth;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Vendors.Apple;

/// <summary>AirPods and Beats paired with this PC, read from the battery broadcast they send to every Apple device
/// nearby (docs/protocols/apple.md). It only listens: nothing is ever sent to the devices. The scan runs while an Apple
/// audio device is paired, so the addresses already around before the pods connect are known.</summary>
/// <remarks>Levels come in 10 % steps, rounded down, and are marked approximate. When no broadcast is fresh, Windows'
/// own value is shown if it has one; otherwise the device stays listed with no level and a note.</remarks>
public sealed class AppleBatteryProvider : IBatteryProvider
{
    public const string VendorName = "Apple";

    /// <summary>How long the first poll waits for a connected device's first broadcast, so a one-shot read (the CLI)
    /// gets a value. Later polls never wait: the provider asks for a refresh when a value arrives.</summary>
    internal static readonly TimeSpan DefaultFirstFrameWait = TimeSpan.FromSeconds(10);

    /// <summary>How long after a failed scan the next start waits, so a scan Windows keeps stopping (the radio is off)
    /// is not restarted, and every vendor polled, in a loop.</summary>
    internal static readonly TimeSpan RestartDelay = TimeSpan.FromMinutes(1);

    private const string TraceChannel = "apple";
    private const string FindMySuffix = " - Find My";
    private const string FallbackName = "AirPods";

    private readonly IAppleBluetooth _bluetooth;
    private readonly TimeProvider _time;
    private readonly TimeSpan _firstFrameWait;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, Slot> _slots = [];
    // Why the scan last failed, until an advertisement proves a scan works again.
    private volatile string? _listenError;
    private DateTimeOffset? _failedAt;
    private TaskCompletionSource? _firstValue;
    private bool _polled;
    private bool _disposed;

    /// <summary>Listens through Windows' Bluetooth stack.</summary>
    public AppleBatteryProvider() : this(new WindowsAppleBluetooth())
    {
    }

    public AppleBatteryProvider(IAppleBluetooth bluetooth, TimeProvider? time = null, TimeSpan? firstFrameWait = null)
    {
        _bluetooth = bluetooth;
        _time = time ?? TimeProvider.System;
        _firstFrameWait = firstFrameWait ?? DefaultFirstFrameWait;
        _bluetooth.AdvertisementReceived += OnAdvertisement;
        _bluetooth.ListeningFailed += OnListeningFailed;
        _bluetooth.ConnectionChanged += OnConnectionChanged;
    }

    public string Vendor => VendorName;

    public event EventHandler? RefreshRequested;

    public static string DeviceId(ulong address) => "apple:" + BluetoothBatteryProperty.FormatAddress(address);

    public async Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? firstValue = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Update();
            if (!_polled && _firstFrameWait > TimeSpan.Zero && _slots.Values.Any(IsWaiting))
            {
                _firstValue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                firstValue = _firstValue.Task;
            }
            _polled = true;
        }

        if (firstValue is not null)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await Task.WhenAny(firstValue, Task.Delay(_firstFrameWait, _time, stop.Token)).ConfigureAwait(false);
            await stop.CancelAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _slots.Values.Where(s => s.Connected == true).Select(Read).ToList();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _firstValue?.TrySetResult();
        }
        _bluetooth.AdvertisementReceived -= OnAdvertisement;
        _bluetooth.ListeningFailed -= OnListeningFailed;
        _bluetooth.ConnectionChanged -= OnConnectionChanged;
        _bluetooth.StopListening();
        _bluetooth.Dispose();
    }

    // The paired devices, the scan and each device's connected state, as Windows reports them now.
    private void Update()
    {
        var paired = FindPaired();
        var seen = new HashSet<ulong>();
        foreach (var device in paired)
        {
            seen.Add(device.Address);
            if (_slots.TryGetValue(device.Address, out var slot) && slot.Device.ModelId == device.ModelId)
            {
                slot.Device = device;
                continue;
            }
            var model = AppleModels.Find(device.ModelId);
            _slots[device.Address] = new Slot(device, model, model is null ? null : new AppleBroadcastTracker(model, _time));
        }
        foreach (var address in _slots.Keys.Where(a => !seen.Contains(a)).ToList()) _slots.Remove(address);

        UpdateListening();

        var now = _time.GetUtcNow();
        foreach (var slot in _slots.Values) UpdateConnection(slot, now);
    }

    private IReadOnlyList<AppleDevice> FindPaired()
    {
        try
        {
            return _bluetooth.FindPaired();
        }
        catch (Exception e)
        {
            Log.Error("Apple: listing paired Bluetooth devices failed", e);
            return [];
        }
    }

    private void UpdateListening()
    {
        if (_slots.Count == 0)
        {
            if (_bluetooth.IsListening) Log.Info("Apple: no AirPods or Beats paired; Bluetooth LE scan stopped");
            _bluetooth.StopListening();
            _listenError = null;
            _failedAt = null;
            return;
        }
        if (_bluetooth.IsListening) return;
        if (_failedAt is { } failed && _time.GetUtcNow() - failed < RestartDelay) return;

        try
        {
            _bluetooth.StartListening();
            foreach (var slot in _slots.Values) slot.Tracker?.ListeningStarted();
            Log.Info("Apple: listening for AirPods and Beats battery broadcasts (passive Bluetooth LE scan)");
        }
        catch (Exception e)
        {
            _listenError = e.Message;
            _failedAt = _time.GetUtcNow();
            Log.Warn($"Apple: the Bluetooth LE scan could not start ({e.GetType().Name}: {e.Message})");
        }
    }

    private void UpdateConnection(Slot slot, DateTimeOffset now)
    {
        bool connected;
        try
        {
            connected = _bluetooth.IsConnected(slot.Device.Address);
        }
        catch (Exception e)
        {
            Log.Warn($"{slot.Device.Name}: cannot tell whether it is connected ({e.Message})");
            connected = false;
        }

        if (slot.Connected == connected) return;
        if (connected) slot.Tracker?.Connected(slot.Connected is null ? null : now); // first look: moment unknown
        else slot.Tracker?.Disconnected();
        slot.Connected = connected;
        Log.Info($"{slot.Device.Name}: {(connected ? "connected" : "not connected")}");
    }

    private bool IsWaiting(Slot slot) =>
        slot.Connected == true && slot.Tracker is { } tracker && !IsAmbiguous(slot) && tracker.Snapshot().Components.Count == 0;

    private bool IsAmbiguous(Slot slot) =>
        _slots.Values.Count(s => s.Connected == true && s.Device.ModelId == slot.Device.ModelId) > 1;

    private DeviceReading Read(Slot slot)
    {
        if (slot.Tracker is not { } tracker)
        {
            return Reading(slot, DeviceStatus.Error, [], $"Apple model 0x{slot.Device.ModelId:X4} is not supported yet");
        }
        if (IsAmbiguous(slot)) return Reading(slot, DeviceStatus.Error, [], "Two connected devices of this model cannot be told apart");

        var snapshot = tracker.Snapshot();
        var windows = WindowsBattery(slot);
        if (snapshot.Components.Count > 0)
        {
            return Reading(slot, DeviceStatus.Online, snapshot.Components, approximate: true) with { Source = DeviceReading.AppleBroadcastSource };
        }

        var silence = snapshot.LastHeard is { } heard
            ? $"No battery broadcast for {(int)(_time.GetUtcNow() - heard).TotalMinutes} min"
            : null;
        if (windows is { } percent)
        {
            return Reading(slot, DeviceStatus.Online, [BatteryComponent.Single(percent, ChargeState.Unknown)],
                (silence ?? "No battery broadcast heard yet") + "; approximate value from Windows", approximate: true) with
            {
                Source = DeviceReading.WindowsHfpSource,
            };
        }
        if (_listenError is { } error) return Reading(slot, DeviceStatus.Error, [], $"Bluetooth LE scan failed: {error}");
        return Reading(slot, DeviceStatus.Online, [], silence ?? "Waiting for its Bluetooth battery broadcast");
    }

    private int? WindowsBattery(Slot slot)
    {
        try
        {
            var value = _bluetooth.ReadWindowsBattery(slot.Device.Address);
            if (Log.TraceFrames) Log.Info($"{slot.Device.Name}: Windows' own battery value: {(value is null ? "none" : $"{value} %")}");
            return value is >= 0 and <= 100 ? value : null;
        }
        catch (Exception e)
        {
            Log.Warn($"{slot.Device.Name}: reading Windows' battery value failed ({e.Message})");
            return null;
        }
    }

    private DeviceReading Reading(Slot slot, DeviceStatus status, IReadOnlyList<BatteryComponent> components, string? detail = null,
        bool approximate = false) =>
        new(DeviceId(slot.Device.Address), VendorName, DisplayName(slot), slot.Model?.Kind ?? DeviceKind.Earbuds, status, components,
            _time.GetUtcNow(), DeviceReading.AppleBroadcastSource, approximate, detail);

    // Windows' name without the " - Find My" a Find My-enabled pair carries; the model's when there is none.
    private static string DisplayName(Slot slot)
    {
        var name = slot.Device.Name.Trim();
        if (name.EndsWith(FindMySuffix, StringComparison.OrdinalIgnoreCase)) name = name[..^FindMySuffix.Length].TrimEnd();
        return name.Length > 0 ? name : slot.Model?.Name ?? FallbackName;
    }

    private void OnAdvertisement(object? sender, BleAdvertisement advertisement)
    {
        if (_listenError is not null) ScanWorks();
        var data = advertisement.Data;
        if (data.Length == 0 || data[0] != AppleProximityProtocol.ProximityPairingType) return; // phones, Macs, AirTags
        Trace(advertisement);
        if (AppleProximityProtocol.Parse(data) is not { } status) return;

        var changed = false;
        TaskCompletionSource? firstValue = null;
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var slot in _slots.Values)
            {
                if (slot.Tracker is { } tracker && tracker.Model.Id == status.ModelId)
                {
                    changed |= tracker.Offer(advertisement.Address, advertisement.Rssi, status);
                }
            }
            if (changed)
            {
                firstValue = _firstValue;
                _firstValue = null;
            }
        }

        if (!changed) return;
        firstValue?.TrySetResult();
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ScanWorks()
    {
        lock (_gate)
        {
            _listenError = null;
            _failedAt = null;
        }
    }

    // Only the first failure since the scan last worked asks for a refresh, so the error is shown once, not in a loop.
    private void OnListeningFailed(object? sender, string reason)
    {
        bool first;
        lock (_gate)
        {
            if (_disposed || _bluetooth.IsListening) return; // a late report from an older scan: a newer one runs
            first = _failedAt is null;
            _listenError = reason;
            _failedAt = _time.GetUtcNow();
        }
        if (first) RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    // Bytes 0-10 only: the rest is encrypted and says nothing to anyone without the key.
    private static void Trace(BleAdvertisement advertisement)
    {
        if (!Log.TraceFrames) return;
        var data = advertisement.Data;
        var rssi = advertisement.Rssi.ToString(CultureInfo.InvariantCulture);
        var kind = advertisement.PublicAddress ? advertisement.Kind + " public" : advertisement.Kind;
        Log.Frame($"{TraceChannel} {BluetoothBatteryProperty.FormatAddress(advertisement.Address)} {rssi} dBm {kind}", false,
            data.AsSpan(0, Math.Min(data.Length, AppleProximityProtocol.PlaintextLength)));
    }

    private sealed class Slot(AppleDevice device, AppleModel? model, AppleBroadcastTracker? tracker)
    {
        public AppleDevice Device { get; set; } = device;

        public AppleModel? Model { get; } = model;

        public AppleBroadcastTracker? Tracker { get; } = tracker;

        /// <summary>Null until the first poll looked.</summary>
        public bool? Connected { get; set; }
    }
}
