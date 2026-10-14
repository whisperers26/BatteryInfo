using BatteryInfo.Core.Diagnostics;
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
    private const int ErrorFileNotFound = 2; // CreateFile on a path removed since enumeration
    private const int ErrorTimeout = 1460; // WindowsHidConnection.Write's 1 s guard expired
    private const string NoAnswer = "No answer from the controller";
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

        // The backend opens (zero access, for caps) only what the filter accepts. The KP70C2 dongle in its asleep
        // identity 20DD:518D is noted and rejected right here, so it is never opened at all.
        var asleep = new HashSet<BetopModel>();
        var present = _hid.Enumerate((vendorId, productId) =>
        {
            if (BetopModels.FindAsleepDongle(vendorId, productId) is not { } sleeping) return IsOnlineModel(vendorId, productId);
            asleep.Add(sleeping);
            return false;
        });

        var byModel = new Dictionary<BetopModel, List<HidDeviceInfo>>();
        foreach (var device in present)
        {
            // Checked again on the result, so a backend that ignored the filter still never gets 20DD opened.
            if (BetopModels.FindAsleepDongle(device.VendorId, device.ProductId) is { } sleeping)
            {
                asleep.Add(sleeping);
                continue;
            }
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

        // Pad asleep or off: the dongle dropped 20BC entirely. Reported from presence alone, with zero I/O.
        foreach (var model in asleep.Where(m => !byModel.ContainsKey(m)).OrderBy(m => m.Name, StringComparer.Ordinal))
            readings.Add(Reading(model, DeviceStatus.Asleep, [], "Controller asleep or off"));
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

    // Device-level failures become statuses; only cancellation and bugs other than a refused frame escape.
    private DeviceReading ReadCollection(BetopModel model, HidDeviceInfo device, CancellationToken cancellationToken)
    {
        try
        {
            return QueryBattery(model, device, cancellationToken);
        }
        catch (HidIoException e) when (e.Win32Error == HidIoException.AccessDenied)
        {
            Log.Warn($"Betop {model.Name}: {e.Message}");
            return Reading(model, DeviceStatus.NeedsAccess, [], "Windows denied access to the controller");
        }
        catch (HidIoException e) when (e.DeviceGone || e.Win32Error == ErrorFileNotFound)
        {
            // The pad fell asleep (the dongle drops 20BC and re-enumerates as 20DD) or was unplugged mid-poll.
            Log.Info($"Betop {model.Name} went away: {e.Message}");
            return Reading(model, DeviceStatus.Asleep, [], "Controller asleep or disconnected");
        }
        catch (HidIoException e) when (e.Win32Error == ErrorTimeout)
        {
            // The dongle stopped taking output reports: its link is in transition, like an unanswered request.
            Log.Info($"Betop {model.Name}: {e.Message}");
            return Reading(model, DeviceStatus.Asleep, [], NoAnswer);
        }
        catch (HidIoException e)
        {
            Log.Warn($"Betop {model.Name}: {e.Message}");
            return Reading(model, DeviceStatus.Error, [], $"Could not read the controller (error {e.Win32Error})");
        }
        catch (UnsafeCommandException e)
        {
            Log.Error($"Betop {model.Name}: a frame was refused by the allow-list", e);
            return Reading(model, DeviceStatus.Error, [], "Internal error: unsafe command refused");
        }
    }

    private DeviceReading QueryBattery(BetopModel model, HidDeviceInfo device, CancellationToken cancellationToken)
    {
        // The safety gate: 20DD:518D has a vendor collection identical to 20BC's, so only the VID tells the asleep
        // (and firmware-update) dongle apart. Nothing but 20BC is ever opened for writing.
        if (device.VendorId != BetopModels.OnlineVendorId)
            throw new UnsafeCommandException($"Refused to open {device.Label} for writing: only VID 20BC may be written to");

        using var connection = new GuardedHidConnection(_hid.Open(device, HidAccess.ReadWrite), _policy);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            connection.FlushQueue();
            connection.Write(BetopProtocol.BuildBatteryRequest());
            if (AwaitBatteryReply(connection, model) is { } status)
                return Reading(model, DeviceStatus.Online, [BatteryComponent.Single(status.Percent, status.Charge)]);
        }
        return Reading(model, DeviceStatus.Asleep, [], NoAnswer);
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
