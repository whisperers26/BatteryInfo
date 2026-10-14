using System.Net.Sockets;
using BatteryInfo.Core.Bluetooth;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.Huawei;

/// <summary>HUAWEI earbuds (FreeClip 2 and siblings) over classic Bluetooth: per poll, for each paired device that
/// Windows reports connected, a short SPP connect / battery request / close (docs/protocols/huawei.md). When the SPP
/// channel is busy or silent, Windows' coarser HFP value is shown instead, marked approximate.</summary>
/// <remarks>Earbuds Windows does not report connected (in their case, out of range) get no RFCOMM attempt, because
/// connecting would page them, and no reading either: the store then shows them as not connected with their last
/// real value. Windows' HFP property is not refreshed while disconnected, so presenting it as a fresh reading would
/// fake recency and add stale points to the history.</remarks>
public sealed class HuaweiBatteryProvider : IBatteryProvider
{
    public const string VendorName = "HUAWEI";

    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);

    /// <summary>How long to wait for the reply to one request. Replies came 21-91 ms after the request.</summary>
    internal static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2);

    private const string SppSource = "SPP";
    private const string HfpSource = "HFP (approx.)";
    private const string TraceChannel = "huawei";
    private const string UnsafeDetail = "Internal error: unsafe command refused";

    /// <summary>Requests per link: the first, then one retry when no reply came (§9).</summary>
    private const int Attempts = 2;

    /// <summary>A bound on reads per request, so a stream that never times out cannot keep a poll spinning.</summary>
    private const int MaxReadsPerAttempt = 64;

    private readonly IHuaweiTransport _transport;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, bool> _lastConnected = [];
    private bool _disposed;

    public HuaweiBatteryProvider(IHuaweiTransport transport, TimeProvider? time = null)
    {
        _transport = transport;
        _time = time ?? TimeProvider.System;
        _transport.ConnectionChanged += OnConnectionChanged;
    }

    public string Vendor => VendorName;

    public event EventHandler? RefreshRequested;

    public static string DeviceId(ulong address) => "huawei:" + BluetoothBatteryProperty.FormatAddress(address);

    public Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var readings = new List<DeviceReading>();
            foreach (var device in FindPaired())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Read(device, cancellationToken) is { } reading) readings.Add(reading);
            }
            return Task.FromResult<IReadOnlyList<DeviceReading>>(readings);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transport.ConnectionChanged -= OnConnectionChanged;
        _transport.Dispose();
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    private IReadOnlyList<HuaweiDevice> FindPaired()
    {
        try
        {
            return _transport.FindPaired();
        }
        catch (Exception e)
        {
            Log.Error("HUAWEI: listing paired Bluetooth devices failed", e);
            return [];
        }
    }

    private DeviceReading? Read(HuaweiDevice device, CancellationToken cancellationToken)
    {
        if (!IsConnected(device)) return null;

        SppFailure failure;
        try
        {
            return Reading(device, DeviceStatus.Online, QuerySpp(device, cancellationToken), SppSource);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnsafeCommandException e)
        {
            Log.Error($"{device.Name}: a frame was refused by the HUAWEI allow-list", e);
            return Reading(device, DeviceStatus.Error, [], SppSource, detail: UnsafeDetail);
        }
        catch (Exception e)
        {
            failure = Classify(e);
            if (failure.Expected) Log.Warn($"{device.Name}: {failure.Reason} ({e.GetType().Name}: {e.Message})");
            else Log.Error($"{device.Name}: {failure.Reason}", e);
        }

        if (WindowsBattery(device) is { } percent)
        {
            return Reading(device, DeviceStatus.Online, [BatteryComponent.Single(percent, ChargeState.Unknown)], HfpSource,
                approximate: true, detail: $"{failure.Reason}; approximate value from Windows");
        }
        return Reading(device, failure.NoAnswer ? DeviceStatus.Asleep : DeviceStatus.Error, [], SppSource, detail: failure.Reason);
    }

    // Never page earbuds Windows does not report connected: when unsure, treat them as disconnected.
    private bool IsConnected(HuaweiDevice device)
    {
        bool connected;
        try
        {
            connected = _transport.IsConnected(device.Address);
        }
        catch (Exception e)
        {
            Log.Warn($"{device.Name}: cannot tell whether it is connected ({e.Message}); not reading it");
            connected = false;
        }

        if (!_lastConnected.TryGetValue(device.Address, out var last) || last != connected)
        {
            Log.Info($"{device.Name}: {(connected ? "connected" : "not connected; SPP skipped")}");
            _lastConnected[device.Address] = connected;
        }
        return connected;
    }

    private IReadOnlyList<BatteryComponent> QuerySpp(HuaweiDevice device, CancellationToken cancellationToken)
    {
        Stream link;
        try
        {
            link = _transport.OpenSpp(device.Address, ConnectTimeout);
        }
        catch (TimeoutException e)
        {
            throw new SppFailureException("Timed out connecting over SPP", noAnswer: true, e);
        }

        // The transport already guards its own stream; this wrapper keeps the check here for any transport.
        using var stream = new GuardedSppStream(link);
        var reader = new HuaweiFrameReader();
        var request = HuaweiProtocol.BuildBatteryRequest();
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Frame(TraceChannel, true, request);
            stream.Write(request);
            stream.Flush();
            if (AwaitBattery(stream, reader, cancellationToken) is { } components) return components;
            if (attempt < Attempts) Log.Warn($"{device.Name}: no battery reply over SPP, asking once more");
        }
        throw new SppFailureException("No battery reply over SPP", noAnswer: true);
    }

    private IReadOnlyList<BatteryComponent>? AwaitBattery(Stream stream, HuaweiFrameReader reader, CancellationToken cancellationToken)
    {
        var buffer = new byte[512];
        var deadline = _time.GetUtcNow() + ReplyTimeout;
        for (var reads = 0; reads < MaxReadsPerAttempt && _time.GetUtcNow() < deadline; reads++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count;
            try
            {
                count = stream.Read(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                continue;
            }
            if (count == 0) throw new SppFailureException("The earbuds closed the SPP link", noAnswer: false);

            reader.Append(buffer.AsSpan(0, count));
            while (reader.TryRead(out var frame))
            {
                Log.Frame(TraceChannel, false, frame.Raw);
                if (!HuaweiProtocol.IsBatteryFrame(frame)) continue; // connect preamble, dual-connect events, ...
                if (HuaweiProtocol.ParseBattery(frame.Payload) is { } components) return components;
                if (frame.Command == HuaweiProtocol.BatteryLevelCommand)
                {
                    throw new SppFailureException("Unreadable battery reply over SPP", noAnswer: false);
                }
            }
        }
        return null;
    }

    private int? WindowsBattery(HuaweiDevice device)
    {
        try
        {
            var value = _transport.ReadWindowsBattery(device.Address);
            return value is >= 0 and <= 100 ? value : null;
        }
        catch (Exception e)
        {
            Log.Warn($"{device.Name}: reading Windows' battery value failed ({e.Message})");
            return null;
        }
    }

    private static SppFailure Classify(Exception e) => e switch
    {
        SppFailureException f => new SppFailure(f.Message, f.NoAnswer, Expected: true),
        SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } =>
            new SppFailure("SPP channel in use by another app", NoAnswer: false, Expected: true),
        SocketException { SocketErrorCode: SocketError.TimedOut or SocketError.HostDown or SocketError.HostUnreachable } s =>
            new SppFailure($"Earbuds did not answer the SPP connect (error {s.ErrorCode})", NoAnswer: true, Expected: true),
        SocketException s => new SppFailure($"SPP connect failed (error {s.ErrorCode})", NoAnswer: false, Expected: true),
        TimeoutException => new SppFailure("SPP timed out", NoAnswer: true, Expected: true),
        IOException => new SppFailure("SPP link dropped", NoAnswer: false, Expected: true),
        _ => new SppFailure("SPP read failed", NoAnswer: false, Expected: false),
    };

    private DeviceReading Reading(HuaweiDevice device, DeviceStatus status, IReadOnlyList<BatteryComponent> components,
        string source, bool approximate = false, string? detail = null) =>
        new(DeviceId(device.Address), VendorName, device.Name, DeviceKind.Earbuds, status, components, _time.GetUtcNow(),
            source, approximate, detail);

    /// <param name="Reason">Short user-facing explanation.</param>
    /// <param name="NoAnswer">The earbuds did not answer (Asleep) rather than something failing (Error).</param>
    /// <param name="Expected">A known failure mode, logged as a warning; otherwise logged as an error.</param>
    private sealed record SppFailure(string Reason, bool NoAnswer, bool Expected);

    private sealed class SppFailureException(string reason, bool noAnswer, Exception? inner = null) : Exception(reason, inner)
    {
        public bool NoAnswer { get; } = noAnswer;
    }
}
