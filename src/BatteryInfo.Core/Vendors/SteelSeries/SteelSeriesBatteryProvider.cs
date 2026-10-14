using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.SteelSeries;

/// <summary>Reads Arctis Nova 5 / 5X headsets through their base station's status collection: 00 B0 out, 00 B0 back
/// with the link state, percent and charging flag (docs/protocols/steelseries.md).</summary>
public sealed class SteelSeriesBatteryProvider : IBatteryProvider
{
    private const string SourceName = "SteelSeries 0xB0";
    private const int Attempts = 2; // one request and one retry
    private const int ErrorFileNotFound = 2; // CreateFile on a path removed since enumeration
    private const int ErrorTimeout = 1460; // WindowsHidConnection.Write's 1 s guard expired
    private const string NoAnswer = "No answer from the base station";
    private const string HeadsetOff = "Headset off or out of range";
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IHidBackend _hid;
    private readonly TimeProvider _time;
    private readonly SteelSeriesCommandPolicy _policy = new();
    private readonly Lock _gate = new();

    public SteelSeriesBatteryProvider()
        : this(new WindowsHidBackend())
    {
    }

    public SteelSeriesBatteryProvider(IHidBackend hid, TimeProvider? time = null)
    {
        _hid = hid;
        _time = time ?? TimeProvider.System;
    }

    public string Vendor => "SteelSeries";

    // Base station arrival and removal reach the monitor through its HID watcher; nothing to add here.
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

        var byModel = new Dictionary<SteelSeriesModel, List<HidDeviceInfo>>();
        foreach (var device in _hid.Enumerate((vendorId, productId) => SteelSeriesModels.Find(vendorId, productId) is not null))
        {
            if (SteelSeriesModels.Find(device.VendorId, device.ProductId) is not { } model) continue;
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

    private DeviceReading ReadModel(SteelSeriesModel model, List<HidDeviceInfo> collections, CancellationToken cancellationToken)
    {
        // Selected by caps, never by COLxx index.
        if (collections.FirstOrDefault(SteelSeriesProtocol.IsStatusCollection) is not { } device)
            return Reading(model, DeviceStatus.Error, [], "Battery interface not found");
        return ReadCollection(model, device, cancellationToken);
    }

    // Device-level failures become statuses; only cancellation and bugs other than a refused frame escape.
    private DeviceReading ReadCollection(SteelSeriesModel model, HidDeviceInfo device, CancellationToken cancellationToken)
    {
        try
        {
            return QueryStatus(model, device, cancellationToken);
        }
        catch (HidIoException e) when (e.Win32Error == HidIoException.AccessDenied)
        {
            Log.Warn($"SteelSeries {model.Name}: {e.Message}");
            return Reading(model, DeviceStatus.NeedsAccess, [], "Windows denied access to the base station");
        }
        catch (HidIoException e) when (e.DeviceGone || e.Win32Error == ErrorFileNotFound)
        {
            Log.Info($"SteelSeries {model.Name} went away: {e.Message}");
            return Reading(model, DeviceStatus.Asleep, [], "Base station disconnected");
        }
        catch (HidIoException e) when (e.Win32Error == ErrorTimeout)
        {
            Log.Info($"SteelSeries {model.Name}: {e.Message}");
            return Reading(model, DeviceStatus.Asleep, [], NoAnswer);
        }
        catch (HidIoException e)
        {
            Log.Warn($"SteelSeries {model.Name}: {e.Message}");
            return Reading(model, DeviceStatus.Error, [], $"Could not read the base station (error {e.Win32Error})");
        }
        catch (UnsafeCommandException e)
        {
            Log.Error($"SteelSeries {model.Name}: a frame was refused by the allow-list", e);
            return Reading(model, DeviceStatus.Error, [], "Internal error: unsafe command refused");
        }
    }

    private DeviceReading QueryStatus(SteelSeriesModel model, HidDeviceInfo device, CancellationToken cancellationToken)
    {
        using var connection = new GuardedHidConnection(_hid.Open(device, HidAccess.ReadWrite), _policy);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            connection.FlushQueue();
            connection.Write(SteelSeriesProtocol.BuildStatusRequest());
            if (AwaitStatusReply(connection) is not { } status) continue;

            // The base station answers for itself; with the headset off the percent is stale.
            return status.Connected
                ? Reading(model, DeviceStatus.Online, [BatteryComponent.Single(status.Percent, status.Charge)])
                : Reading(model, DeviceStatus.Asleep, [], HeadsetOff);
        }
        return Reading(model, DeviceStatus.Asleep, [], NoAnswer);
    }

    // Skips every other frame on the handle, e.g. SteelSeries GG's own replies.
    private SteelSeriesStatus? AwaitStatusReply(IHidConnection connection)
    {
        var started = _time.GetTimestamp();
        while (true)
        {
            var remaining = ReplyTimeout - _time.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return null;
            if (connection.Read(remaining) is not { } report) return null;
            if (SteelSeriesProtocol.TryParseStatus(report, out var status)) return status;
        }
    }

    private DeviceReading Reading(SteelSeriesModel model, DeviceStatus status, IReadOnlyList<BatteryComponent> components,
        string? detail = null) =>
        new(model.DeviceId, Vendor, model.DisplayName, DeviceKind.Headset, status, components, _time.GetUtcNow(),
            SourceName, Detail: detail);
}
