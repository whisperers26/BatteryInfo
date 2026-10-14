using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.Betop;

/// <summary>Reads Betop gen-4 pads (KP70C2 and its plain-byte siblings) through the dongle's or the cable's vendor
/// collection: 02 15 out, 02 15 back with the percent in byte 2 (docs/protocols/betop.md).</summary>
public sealed class BetopBatteryProvider : IBatteryProvider
{
    private const string SourceName = "Betop 0x15";
    private const int Attempts = 3; // one request and up to two retries
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IHidBackend _hid;
    private readonly TimeProvider _time;
    private readonly BetopCommandPolicy _policy = new();
    private readonly Lock _gate = new();

    public BetopBatteryProvider()
        : this(new WindowsHidBackend())
    {
    }

    public BetopBatteryProvider(IHidBackend hid, TimeProvider? time = null)
    {
        _hid = hid;
        _time = time ?? TimeProvider.System;
    }

    public string Vendor => "Betop";

    // Device changes reach the monitor through its HID arrival/removal watcher; nothing to add here.
    public event EventHandler? RefreshRequested
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            lock (_gate) return Task.FromResult<IReadOnlyList<DeviceReading>>(Poll(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<DeviceReading>>(cancellationToken);
        }
    }

    public void Dispose()
    {
    }

    private List<DeviceReading> Poll(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var byModel = new Dictionary<BetopModel, List<HidDeviceInfo>>();
        foreach (var device in _hid.Enumerate(IsOnlineModel))
        {
            if (device.VendorId != BetopModels.OnlineVendorId || BetopModels.FindByProductId(device.ProductId) is not { } model) continue;
            if (!byModel.TryGetValue(model, out var collections)) byModel[model] = collections = [];
            collections.Add(device);
        }

        var readings = new List<DeviceReading>();
        foreach (var (model, collections) in byModel.OrderBy(m => m.Key.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            readings.Add(ReadModel(model, collections, cancellationToken));
        }
        return readings;
    }

    private static bool IsOnlineModel(ushort vendorId, ushort productId) =>
        vendorId == BetopModels.OnlineVendorId && BetopModels.FindByProductId(productId) is not null;

    private DeviceReading ReadModel(BetopModel model, List<HidDeviceInfo> collections, CancellationToken cancellationToken)
    {
        // Selected by caps, never by COLxx index. The cable path first: it does not depend on the radio link.
        var candidates = collections
            .Where(BetopProtocol.IsVendorCollection)
            .OrderBy(c => c.ProductId == model.WiredProductId ? 0 : 1)
            .ToList();
        if (candidates.Count == 0) return Reading(model, DeviceStatus.Error, [], "Battery interface not found");

        DeviceReading? reading = null;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reading = ReadCollection(model, candidate, cancellationToken);
            if (reading.Status == DeviceStatus.Online) break;
        }
        return reading!;
    }

    private DeviceReading ReadCollection(BetopModel model, HidDeviceInfo device, CancellationToken cancellationToken)
    {
        using var connection = new GuardedHidConnection(_hid.Open(device, HidAccess.ReadWrite), _policy);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            connection.FlushQueue();
            connection.Write(BetopProtocol.BuildBatteryRequest());
            if (AwaitBatteryReply(connection, model) is { } status)
                return Reading(model, DeviceStatus.Online, [BatteryComponent.Single(status.Percent, status.Charge)]);
        }
        return Reading(model, DeviceStatus.Asleep, [], "No answer from the controller");
    }

    // Skips every other frame on the handle: BetopGame's 0x25/0x11/0x21 replies, unsolicited 0x55 link changes.
    private BetopStatus? AwaitBatteryReply(IHidConnection connection, BetopModel model)
    {
        var started = _time.GetTimestamp();
        while (true)
        {
            var remaining = ReplyTimeout - _time.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return null;
            if (connection.Read(remaining) is not { } report) return null;
            if (BetopProtocol.TryParseBattery(report, model.ChargeInLowNibble, out var status)) return status;
        }
    }

    private DeviceReading Reading(BetopModel model, DeviceStatus status, IReadOnlyList<BatteryComponent> components,
        string? detail = null) =>
        new(model.DeviceId, Vendor, model.DisplayName, DeviceKind.Controller, status, components, _time.GetUtcNow(),
            SourceName, Detail: detail);
}
