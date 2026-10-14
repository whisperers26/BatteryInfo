using System.Globalization;
using System.Text;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.Rog;

/// <summary>Reads ROG devices (docs/protocols/rog.md): each OMNI-class receiver is scanned with A0 00 and every paired
/// keyboard (12 03, 12 01) or mouse (12 00 02, 12 07) is read through the receiver's slot collection; directly attached
/// devices (wired Azoth X, own-dongle mice, Cetra) are read on their own control collection. Only the vendor control
/// collections (usage pages FF00/FF01/FF02) are ever opened, never the FFC0-FFC4 event collections, and every frame goes
/// through <see cref="RogCommandPolicy"/>.</summary>
public sealed class RogBatteryProvider : IBatteryProvider
{
    private const string VendorName = "ROG";
    private const ushort ReceiverControlPage = 0xFF02;
    private const ushort KeyboardControlPage = 0xFF00;
    private const ushort MouseControlPage = 0xFF01;

    private const string AsleepDetail = "Asleep or out of range";
    private const string UnexpectedDetail = "Unexpected reply";
    private const string UnsafeDetail = "Internal error: unsafe command refused";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan MouseExistTimeout = TimeSpan.FromMilliseconds(1000);

    private readonly IHidBackend _hid;
    private readonly TimeProvider _time;
    private readonly ICommandPolicy _policy;
    private readonly object _gate = new();

    public RogBatteryProvider()
        : this(new WindowsHidBackend())
    {
    }

    public RogBatteryProvider(IHidBackend hid, TimeProvider? time = null)
        : this(hid, time, new RogCommandPolicy())
    {
    }

    /// <summary>Test seam: a different allow-list, to exercise the refused-command path.</summary>
    internal RogBatteryProvider(IHidBackend hid, TimeProvider? time, ICommandPolicy policy)
    {
        _hid = hid;
        _time = time ?? TimeProvider.System;
        _policy = policy;
    }

    public string Vendor => VendorName;

    // ROG pushes no battery events this app listens to (the event collections are never opened).
    public event EventHandler? RefreshRequested
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult(Poll(cancellationToken));
    }

    public void Dispose()
    {
    }

    private IReadOnlyList<DeviceReading> Poll(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collections = _hid.Enumerate(RogModels.IsSupported);
        var found = new List<(DeviceReading Reading, bool Direct)>();

        foreach (var receiver in collections.Where(IsReceiverControl))
        {
            cancellationToken.ThrowIfCancellationRequested();
            found.AddRange(PollReceiver(receiver, collections, cancellationToken).Select(r => (r, false)));
        }

        foreach (var (device, model) in DirectDevices(collections))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var serial = CleanSerial(device.Serial) ?? ProductIdText(device.ProductId);
            found.Add((ReadDevice(device, model, DeviceId(model, serial), device.OutputReportId ?? 0, model.Link == RogLink.Usb), true));
        }

        return Merge(found);
    }

    // --- Receivers ---

    private List<DeviceReading> PollReceiver(HidDeviceInfo receiver, IReadOnlyList<HidDeviceInfo> collections,
        CancellationToken cancellationToken)
    {
        var model = RogModels.Find(receiver.ProductId)!;
        var serial = CleanSerial(receiver.Serial);
        IReadOnlyList<RogSlot> slots;
        try
        {
            using var connection = Open(receiver);
            var channel = new RogChannel(connection, receiver.OutputReportId ?? 0, _time);
            serial ??= ReadSerial(channel);

            var scan = channel.Transact(RogProtocol.ScanForPairedDevices, CommandTimeout);
            if (scan.Outcome != RogOutcome.Reply)
            {
                Log.Warn($"ROG: {receiver.Label} did not answer the device scan ({scan.Outcome}).");
                return [ReceiverReading(model, serial ?? ProductIdText(receiver.ProductId), DeviceStatus.Error, "Receiver did not answer")];
            }
            slots = RogProtocol.ParseScan(scan.Report);
        }
        catch (HidIoException e) when (e.Win32Error == HidIoException.AccessDenied)
        {
            return [ReceiverReading(model, serial ?? ProductIdText(receiver.ProductId), DeviceStatus.NeedsAccess, "Windows denied access to the receiver")];
        }
        catch (HidIoException e) when (e.DeviceGone)
        {
            Log.Info($"ROG: {receiver.Label} went away during the scan: {e.Message}");
            return [];
        }
        catch (HidIoException e)
        {
            Log.Warn($"ROG: scanning {receiver.Label} failed: {e.Message}");
            return [ReceiverReading(model, serial ?? ProductIdText(receiver.ProductId), DeviceStatus.Error, IoDetail(e))];
        }
        catch (UnsafeCommandException e)
        {
            Log.Error($"ROG: a command to {receiver.Label} was refused", e);
            return [ReceiverReading(model, serial ?? ProductIdText(receiver.ProductId), DeviceStatus.Error, UnsafeDetail)];
        }

        serial ??= ProductIdText(receiver.ProductId);
        var readings = new List<DeviceReading>();
        foreach (var slot in slots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slotModel = RogModels.ForSlot(slot);
            if (slotModel.Kind is not (DeviceKind.Keyboard or DeviceKind.Mouse))
            {
                Log.Warn($"ROG: {receiver.Label} lists device {slot.ProductId:X4} on unknown report {slot.ControlReportId}; skipped.");
                continue;
            }

            var id = DeviceId(slotModel, serial);
            var control = collections.FirstOrDefault(c => SameInterface(c, receiver) &&
                c.UsagePage is KeyboardControlPage or MouseControlPage && c.Usage == 1 && IsUsable(c) &&
                c.OutputReportId == slot.ControlReportId);
            if (control is null)
            {
                readings.Add(Reading(id, slotModel, DeviceStatus.Error, [], "Receiver interface for this device not found"));
                continue;
            }
            readings.Add(ReadDevice(control, slotModel, id, slot.ControlReportId, onCable: false));
        }
        return readings;
    }

    private static string? ReadSerial(RogChannel channel)
    {
        var reply = channel.Transact(RogProtocol.ReceiverSerialNumber, CommandTimeout);
        return reply.Outcome == RogOutcome.Reply ? CleanSerial(RogProtocol.ParseSerial(reply.Report)) : null;
    }

    private DeviceReading ReceiverReading(RogModel receiver, string serial, DeviceStatus status, string detail) =>
        new(DeviceId(receiver, serial), VendorName, receiver.Name, DeviceKind.Other, status, [], _time.GetUtcNow(), "ROG A0 00",
            Detail: detail);

    // --- One device on one control collection ---

    private DeviceReading ReadDevice(HidDeviceInfo control, RogModel model, string id, byte reportId, bool onCable)
    {
        try
        {
            using var connection = Open(control);
            var channel = new RogChannel(connection, reportId, _time);
            return model.Kind switch
            {
                DeviceKind.Keyboard => ReadKeyboard(channel, model, id, onCable),
                DeviceKind.Mouse => ReadMouse(channel, model, id, onCable),
                _ => ReadCetra(channel, model, id),
            };
        }
        catch (HidIoException e) when (e.Win32Error == HidIoException.AccessDenied)
        {
            return Reading(id, model, DeviceStatus.NeedsAccess, [], "Windows denied access");
        }
        catch (HidIoException e) when (e.DeviceGone)
        {
            Log.Info($"ROG: {model.Name} on {control.Label} stopped answering: {e.Message}");
            return Reading(id, model, DeviceStatus.Asleep, [], "Not responding");
        }
        catch (HidIoException e)
        {
            Log.Warn($"ROG: reading {model.Name} on {control.Label} failed: {e.Message}");
            return Reading(id, model, DeviceStatus.Error, [], IoDetail(e));
        }
        catch (UnsafeCommandException e)
        {
            Log.Error($"ROG: a command to {model.Name} on {control.Label} was refused", e);
            return Reading(id, model, DeviceStatus.Error, [], UnsafeDetail);
        }
    }

    private DeviceReading ReadKeyboard(RogChannel channel, RogModel model, string id, bool onCable)
    {
        if (!onCable)
        {
            var link = channel.Transact(RogProtocol.KeyboardConnectionType, CommandTimeout);
            if (link.Outcome != RogOutcome.Reply) return Reading(id, model, DeviceStatus.Asleep, [], AsleepDetail);
            switch (RogProtocol.ParseConnectionType(link.Report))
            {
                case null:
                    return Reading(id, model, DeviceStatus.Error, [], UnexpectedDetail);
                case RogConnectionType.NotPresent:
                    return Reading(id, model, DeviceStatus.Asleep, [], "Not connected to the receiver");
                case RogConnectionType.Wired:
                    onCable = true;
                    break;
            }
        }

        var reply = channel.Transact(RogProtocol.KeyboardPowerInfo, CommandTimeout);
        if (reply.Outcome != RogOutcome.Reply) return Reading(id, model, DeviceStatus.Asleep, [], AsleepDetail);
        return RogProtocol.ParseKeyboardPower(reply.Report) is { } power
            ? Online(id, model, power, onCable)
            : Reading(id, model, DeviceStatus.Error, [], UnexpectedDetail);
    }

    private DeviceReading ReadMouse(RogChannel channel, RogModel model, string id, bool onCable)
    {
        if (!onCable)
        {
            var exist = channel.Transact(RogProtocol.MouseExistInfo, MouseExistTimeout);
            if (exist.Outcome != RogOutcome.Reply) return Reading(id, model, DeviceStatus.Asleep, [], AsleepDetail);
            switch (RogProtocol.ParseMouseExists(exist.Report))
            {
                case null:
                    return Reading(id, model, DeviceStatus.Error, [], UnexpectedDetail);
                case false:
                    return Reading(id, model, DeviceStatus.Asleep, [], AsleepDetail);
            }
        }

        var reply = channel.Transact(RogProtocol.MousePowerInfo, CommandTimeout);
        if (reply.Outcome != RogOutcome.Reply) return Reading(id, model, DeviceStatus.Asleep, [], AsleepDetail);
        return RogProtocol.ParseMousePower(reply.Report) switch
        {
            null => Reading(id, model, DeviceStatus.Error, [], UnexpectedDetail),
            { Asleep: true } => Reading(id, model, DeviceStatus.Asleep, [], AsleepDetail),
            { } power => Online(id, model, power, onCable),
        };
    }

    private DeviceReading ReadCetra(RogChannel channel, RogModel model, string id)
    {
        var level = channel.Transact(RogProtocol.CetraBatteryLevel, CommandTimeout);
        if (level.Outcome != RogOutcome.Reply) return Reading(id, model, DeviceStatus.Asleep, [], AsleepDetail);
        if (RogProtocol.ParseCetraBattery(level.Report) is not { } battery)
            return Reading(id, model, DeviceStatus.Error, [], UnexpectedDetail);
        if (battery is { Left: null, Right: null, Case: null })
            return Reading(id, model, DeviceStatus.Asleep, [], "Earbuds not connected");

        var status = channel.Transact(RogProtocol.CetraChargingStatus, CommandTimeout);
        var charging = status.Outcome == RogOutcome.Reply ? RogProtocol.ParseCetraCharging(status.Report) : null;
        return Reading(id, model, DeviceStatus.Online,
            [
                new BatteryComponent("Left", battery.Left, Charge(charging?.Left)),
                new BatteryComponent("Right", battery.Right, Charge(charging?.Right)),
                new BatteryComponent("Case", battery.Case, Charge(charging?.Case)),
            ],
            detail: null);

        static ChargeState Charge(bool? charging) => charging switch
        {
            true => ChargeState.Charging,
            false => ChargeState.Discharging,
            null => ChargeState.Unknown,
        };
    }

    private DeviceReading Online(string id, RogModel model, RogPower power, bool onCable)
    {
        // On a cable and not charging means held on external power: full at 100 %, otherwise not charging.
        var charge = power.Charging switch
        {
            true => ChargeState.Charging,
            false when onCable => power.Percent == 100 ? ChargeState.Full : ChargeState.NotCharging,
            false => ChargeState.Discharging,
            null => ChargeState.Unknown,
        };
        return power.Percent is { } percent
            ? Reading(id, model, DeviceStatus.Online, [BatteryComponent.Single(percent, charge)], detail: null)
            : Reading(id, model, DeviceStatus.Online, [], "Battery level not reported");
    }

    private DeviceReading Reading(string id, RogModel model, DeviceStatus status, IReadOnlyList<BatteryComponent> components,
        string? detail) =>
        new(id, VendorName, model.Name, model.Kind, status, components, _time.GetUtcNow(), Source(model.Kind), Detail: detail);

    // --- Selection and helpers ---

    private IHidConnection Open(HidDeviceInfo device)
    {
        // Event collections stream live key state; selection never picks them, this is a second line of defence.
        if (device.UsagePage is >= 0xFFC0 and <= 0xFFC4)
            throw new InvalidOperationException($"Refusing to open the ROG event collection {device.Label}.");
        return new GuardedHidConnection(_hid.Open(device, HidAccess.ReadWrite), _policy);
    }

    private static bool IsUsable(HidDeviceInfo c) =>
        c.Accessible && c.OutputReportLength >= RogProtocol.HeaderLength && c.InputReportLength > 0;

    private static bool IsReceiverControl(HidDeviceInfo c) =>
        RogModels.Find(c.ProductId) is { Link: RogLink.Receiver } && c.UsagePage == ReceiverControlPage && c.Usage == 1 && IsUsable(c);

    private static IEnumerable<(HidDeviceInfo Device, RogModel Model)> DirectDevices(IReadOnlyList<HidDeviceInfo> collections)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in collections)
        {
            if (RogModels.Find(c.ProductId) is not { Link: RogLink.Usb or RogLink.Wireless } model) continue;
            var page = model.Kind == DeviceKind.Mouse ? MouseControlPage : KeyboardControlPage;
            if (c.UsagePage != page || c.Usage != 1 || !IsUsable(c)) continue;
            if (model.Kind == DeviceKind.Earbuds && c.OutputReportId != RogProtocol.CetraReportId) continue;
            if (!seen.Add($"{c.ProductId:X4}|{c.Serial ?? c.ParentInstanceId ?? c.Path}")) continue;
            yield return (c, model);
        }
    }

    // Collections of one USB interface share the parent devnode.
    private static bool SameInterface(HidDeviceInfo a, HidDeviceInfo b) =>
        a.ParentInstanceId is not null && b.ParentInstanceId is not null
            ? string.Equals(a.ParentInstanceId, b.ParentInstanceId, StringComparison.OrdinalIgnoreCase)
            : a.VendorId == b.VendorId && a.ProductId == b.ProductId && a.InterfaceNumber == b.InterfaceNumber;

    // One device reached two ways (wired and through the receiver) is one reading: an online one first, then the
    // directly attached one.
    private static List<DeviceReading> Merge(List<(DeviceReading Reading, bool Direct)> found)
    {
        var best = new Dictionary<string, (DeviceReading Reading, bool Direct)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var candidate in found)
        {
            var id = candidate.Reading.DeviceId;
            if (!best.TryGetValue(id, out var current))
            {
                best[id] = candidate;
                order.Add(id);
            }
            else if (Rank(candidate) > Rank(current))
            {
                best[id] = candidate;
            }
        }
        return order.Select(id => best[id].Reading).ToList();

        static int Rank((DeviceReading Reading, bool Direct) r) => (r.Reading.Status == DeviceStatus.Online ? 2 : 0) + (r.Direct ? 1 : 0);
    }

    private static string DeviceId(RogModel model, string serial) => $"rog:{model.Slug}:{serial}";

    private static string ProductIdText(ushort productId) => productId.ToString("X4", CultureInfo.InvariantCulture);

    private static string? CleanSerial(string? serial)
    {
        if (serial is null) return null;
        var clean = new StringBuilder();
        foreach (var ch in serial.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.') clean.Append(ch);
        }
        return clean.Length == 0 ? null : clean.ToString();
    }

    private static string Source(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "ROG 12 07",
        DeviceKind.Earbuds => "ROG CC 12 12",
        _ => "ROG 12 01",
    };

    private static string IoDetail(HidIoException e) => $"Read failed (error {e.Win32Error})";
}

internal enum RogOutcome
{
    Reply,
    Rejected,
    NoReply,
}

internal readonly record struct RogExchange(RogOutcome Outcome, byte[]? Report);

/// <summary>Request/reply on one opened ROG collection (rog.md §3): flush, write the full output report, then read until
/// the matching reply, an FF AA reject or the timeout, skipping other programs' frames; one retry when nothing answers.
/// A copy of the last accepted reply header arriving within 100 ms of it is a duplicate and is ignored.</summary>
internal sealed class RogChannel(IHidConnection connection, byte reportId, TimeProvider time)
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(100);
    private const int Attempts = 2;
    private const int MaxReadsPerAttempt = 64;

    private byte[]? _lastHeader;
    private DateTimeOffset _lastReplyAt;

    public RogExchange Transact(RogCommand command, TimeSpan timeout)
    {
        var request = RogProtocol.BuildRequest(connection.Device.OutputReportLength, reportId, command);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            connection.FlushQueue();
            connection.Write(request);
            var deadline = time.GetUtcNow() + timeout;
            for (var reads = 0; reads < MaxReadsPerAttempt; reads++)
            {
                var remaining = deadline - time.GetUtcNow();
                if (remaining <= TimeSpan.Zero || connection.Read(remaining) is not { } report) break;

                switch (RogProtocol.Classify(report, reportId, command))
                {
                    case RogFrame.Rejected:
                        return new RogExchange(RogOutcome.Rejected, report);
                    case RogFrame.Reply when !IsDuplicate(report, command):
                        _lastHeader = report[..HeaderLength(command)];
                        _lastReplyAt = time.GetUtcNow();
                        return new RogExchange(RogOutcome.Reply, report);
                }
            }
        }
        return new RogExchange(RogOutcome.NoReply, null);
    }

    private bool IsDuplicate(byte[] report, RogCommand command) =>
        _lastHeader is not null && time.GetUtcNow() - _lastReplyAt < DuplicateWindow &&
        report.AsSpan(0, HeaderLength(command)).SequenceEqual(_lastHeader);

    // The A0 00 scan echoes only cmd and sub; everything else echoes the full 4-byte header.
    private static int HeaderLength(RogCommand command) => command == RogProtocol.ScanForPairedDevices ? 3 : RogProtocol.HeaderLength;
}
