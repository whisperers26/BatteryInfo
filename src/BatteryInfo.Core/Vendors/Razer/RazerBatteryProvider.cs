using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.Razer;

/// <summary>Reads Razer mice through the 90-byte feature report on their MI_00 mouse collection
/// (docs/protocols/razer.md): 07/84 charging status, then 07/80 battery level.</summary>
public sealed class RazerBatteryProvider : IBatteryProvider
{
    public const string VendorName = "Razer";
    public const string Source = "Razer 07/80";

    // razer.md §6: wait 10-30 ms after the send, then poll every 10 ms for about 300 ms; on 0x03/0x04 (or no
    // usable reply) resend up to 2 more times, about 150 ms apart.
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(20);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    internal const int PollsPerSend = 30;
    internal static readonly TimeSpan ResendGap = TimeSpan.FromMilliseconds(150);
    internal const int Sends = 3;

    private readonly IHidBackend _hid;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    public RazerBatteryProvider()
        : this(new WindowsHidBackend())
    {
    }

    public RazerBatteryProvider(IHidBackend hid, TimeProvider? time = null)
    {
        _hid = hid;
        _time = time ?? TimeProvider.System;
    }

    public string Vendor => VendorName;

    // Never raised: device arrival is caught by the monitor's HID device-change watcher.
    public event EventHandler? RefreshRequested
    {
        add { }
        remove { }
    }

    /// <summary>Test seam: the allow-list every frame is checked against.</summary>
    internal ICommandPolicy Policy { get; init; } = new RazerCommandPolicy();

    /// <summary>Test seam: blocks for the delay or until the token is cancelled.</summary>
    internal Action<TimeSpan, CancellationToken> Wait { get; init; } = (delay, ct) => ct.WaitHandle.WaitOne(delay);

    public Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            lock (_gate) return Task.FromResult(Poll(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<DeviceReading>>(cancellationToken);
        }
    }

    public void Dispose()
    {
    }

    /// <summary>The MI_00 mouse TLC that carries the Razer report: UP 1, U 2, 91-byte feature report with report
    /// id 0, and not one of the RZVIRTUAL children Razer's driver adds (razer.md §2). Matched by caps, never by
    /// collection index.</summary>
    internal static bool IsReportCollection(HidDeviceInfo device) =>
        device.InterfaceNumber == 0
        && !device.Path.Contains("&col", StringComparison.OrdinalIgnoreCase)
        && !device.Path.Contains("rzvirtual", StringComparison.OrdinalIgnoreCase)
        && device.InstanceId?.Contains("RZVIRTUAL", StringComparison.OrdinalIgnoreCase) != true
        && device.UsagePage == 0x0001
        && device.Usage == 0x0002
        && device.FeatureReportLength == RazerProtocol.BufferLength
        && device.FeatureReportId is null or 0;

    private IReadOnlyList<DeviceReading> Poll(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var readings = new List<DeviceReading>();
        foreach (var (model, link, device) in SelectDevices(_hid.Enumerate(RazerModels.IsKnownUsb)))
        {
            ct.ThrowIfCancellationRequested();
            readings.Add(Read(model, link, device, ct));
        }
        return readings;
    }

    // One collection per model; the cable PID wins over the dongle when both are present (razer.md §7).
    private static IEnumerable<(RazerModel Model, RazerLink Link, HidDeviceInfo Device)> SelectDevices(
        IEnumerable<HidDeviceInfo> devices) =>
        devices
            .Where(IsReportCollection)
            .Select(d => (Match: RazerModels.FindUsb(d.VendorId, d.ProductId), Device: d))
            .Where(c => c.Match is not null)
            .Select(c => (c.Match!.Value.Model, c.Match.Value.Link, c.Device))
            .GroupBy(c => c.Model)
            .Select(g => g.OrderBy(c => c.Link == RazerLink.Wired ? 0 : 1).First());

    private DeviceReading Read(RazerModel model, RazerLink link, HidDeviceInfo device, CancellationToken ct)
    {
        using var connection = new GuardedHidConnection(_hid.Open(device, HidAccess.None), Policy);

        if (link == RazerLink.Dongle)
        {
            // The dongle answers 00/BF itself, so an unlinked mouse is known without waiting out 07/xx timeouts.
            // Optional: anything but a clear "not linked" goes on to the battery queries.
            var links = Exchange(connection, RazerProtocol.WirelessStatusRequest(model.TransactionId), ct);
            if (links.Outcome == RazerReplyKind.Success && IsUnlinked(model, RazerProtocol.ParseWirelessStatus(links.Reply)))
                return Reading(model, DeviceStatus.Asleep, detail: "Not connected to its dongle (off or out of range)");
        }

        var charging = Exchange(connection, RazerProtocol.ChargingStatusRequest(model.TransactionId), ct);
        // A mouse that does not answer 07/84 will not answer 07/80 either; a rejected 07/84 only loses the state.
        if (charging.Outcome is RazerReplyKind.NoResponse or RazerReplyKind.Pending) return NotAnswered(model, charging.Outcome);
        var charge = charging.Outcome == RazerReplyKind.Success ? RazerProtocol.ParseChargeState(charging.Reply) : ChargeState.Unknown;

        var battery = Exchange(connection, RazerProtocol.BatteryLevelRequest(model.TransactionId), ct);
        if (battery.Outcome is not RazerReplyKind.Success) return NotAnswered(model, battery.Outcome);

        return Reading(model, DeviceStatus.Online, [BatteryComponent.Single(RazerProtocol.ParseBatteryPercent(battery.Reply), charge)]);
    }

    // The table lists this model's slot (seen as 02 | 00 00 D7 | 00 FF FF) and that slot is not linked.
    private static bool IsUnlinked(RazerModel model, IReadOnlyList<RazerLinkSlot> slots)
    {
        var ours = slots.Where(s => s.ProductId == model.DonglePid || s.ProductId == model.WiredPid).ToList();
        return ours.Count > 0 && !ours.Any(s => s.Linked);
    }

    /// <summary>Sends <paramref name="request"/> and polls the mailbox until our reply settles. Busy, new, foreign
    /// and bad-CRC buffers are polled again, never resent while the poll lasts (receivers drop commands sent while
    /// relaying). 0x03/0x04, or a poll that runs out, is resent up to <see cref="Sends"/> times in all. The outcome
    /// is the last settled status, or <see cref="RazerReplyKind.Pending"/> when nothing usable ever arrived.</summary>
    private (RazerReplyKind Outcome, byte[] Reply) Exchange(IHidConnection connection, byte[] request, CancellationToken ct)
    {
        var reply = new byte[RazerProtocol.BufferLength];
        var outcome = RazerReplyKind.Pending;
        for (var send = 0; send < Sends; send++)
        {
            if (send > 0) Pause(ResendGap, ct);
            connection.SetFeature(request);
            Pause(SettleDelay, ct);
            for (var poll = 0; poll < PollsPerSend; poll++)
            {
                if (poll > 0) Pause(PollInterval, ct);
                Array.Clear(reply); // byte 0 = report id 0
                connection.GetFeature(reply);
                var kind = RazerProtocol.Classify(request, reply);
                if (kind is RazerReplyKind.Success or RazerReplyKind.NotSupported) return (kind, reply);
                if (kind is RazerReplyKind.Failure or RazerReplyKind.NoResponse)
                {
                    outcome = kind;
                    break;
                }
            }
        }
        return (outcome, reply);
    }

    private void Pause(TimeSpan delay, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Wait(delay, ct);
        ct.ThrowIfCancellationRequested();
    }

    private DeviceReading NotAnswered(RazerModel model, RazerReplyKind outcome) => outcome switch
    {
        RazerReplyKind.NoResponse => Reading(model, DeviceStatus.Asleep, detail: "Not responding (asleep, off or out of range)"),
        RazerReplyKind.NotSupported => Reading(model, DeviceStatus.Error, detail: "The mouse does not support battery queries"),
        RazerReplyKind.Failure => Reading(model, DeviceStatus.Error, detail: "The mouse rejected the battery query"),
        _ => Reading(model, DeviceStatus.Asleep, detail: "No valid reply from the mouse"),
    };

    private DeviceReading Reading(RazerModel model, DeviceStatus status, IReadOnlyList<BatteryComponent>? components = null,
        string? detail = null) =>
        new(model.DeviceId, VendorName, model.Name, model.Kind, status, components ?? [], _time.GetUtcNow(), Source,
            Detail: detail);
}
