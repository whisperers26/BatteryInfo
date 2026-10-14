using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.Logitech;

/// <summary>Reads Logitech devices over HID++ (docs/protocols/logitech.md §10). For each LIGHTSPEED or Unifying
/// receiver the B5 registers list the paired slots; each slot is pinged, its features are resolved with IRoot, and the
/// battery is read from 0x1004 (else 0x1000, 0x1001, 0x1F20). Every frame goes through
/// <see cref="GuardedHidConnection"/> with the <see cref="LogitechCommandPolicy"/>, which only allows a feature call
/// once this provider has registered the feature id it resolved for that index.
/// <para>A device on its own cable (046D:C0A8) is the same HID++ device at index 0xFF; it reports the same unit id as
/// its receiver slot, so the two paths give one reading.</para>
/// <para>Resolved features, name and unit id are cached per device and reused by later polls, which then only ping,
/// re-check the battery feature's index with IRoot, and read the battery. The cache is dropped when the receiver
/// reports another device in the slot or disappears, and its feature indexes when the device reconnects (0x1D4B
/// event, receiver 0x41 notification, new protocol version), reports error 6, or the battery index moved.</para></summary>
public sealed class LogitechBatteryProvider : IBatteryProvider
{
    /// <summary>The HID++ software id in every request. 0x0C is not used by the Logitech tools known to share the
    /// receiver (0x01 kernel, 0x07 OpenRGB, 0x0A LGSTray, 0x0B Solaar, 0x0D G HUB, 0x0F firmware).</summary>
    public const byte SoftwareId = 0x0C;

    private const string VendorName = "Logitech";
    private const string GenericSource = "HID++";
    private const int MaxSlots = 6;
    private const int MaxBusyRetries = 3;
    private const int MaxNameLength = 64;
    private const string NotResponding = "Not responding (asleep or off)";

    private static readonly SlotIdentity WiredIdentity = new(null, null, null, 0);

    // In the order this app prefers them; a device normally has only one.
    private static readonly ushort[] BatteryFeatures =
        [HidppFeature.UnifiedBattery, HidppFeature.BatteryStatus, HidppFeature.BatteryVoltage, HidppFeature.AdcMeasurement];

    private readonly IHidBackend _hid;
    private readonly TimeProvider _time;
    private readonly HidppTimings _timings;
    private readonly LogitechCommandPolicy _policy = new();
    private readonly object _gate = new();
    private readonly Dictionary<(string Channel, byte Index), KnownDevice> _devices = [];

    public LogitechBatteryProvider()
        : this(new WindowsHidBackend())
    {
    }

    public LogitechBatteryProvider(IHidBackend hid, TimeProvider? time = null)
        : this(hid, time, HidppTimings.Default)
    {
    }

    internal LogitechBatteryProvider(IHidBackend hid, TimeProvider? time, HidppTimings timings)
    {
        _hid = hid;
        _time = time ?? TimeProvider.System;
        _timings = timings;
    }

    public string Vendor => VendorName;

    /// <summary>Never raised: the HID++ handles are open only during a poll.</summary>
    public event EventHandler? RefreshRequested
    {
        add { }
        remove { }
    }

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

    private IReadOnlyList<DeviceReading> Poll(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var channels = HidppChannel.Pair(_hid.Enumerate(LogitechModels.IsHidppProduct));
        ForgetAbsentChannels(channels);

        var readings = new List<DeviceReading>();
        foreach (var collections in channels)
        {
            ct.ThrowIfCancellationRequested();
            readings.AddRange(PollChannel(collections, ct));
        }
        return Deduplicate(readings);
    }

    // A mouse on its cable can also be paired to a receiver: both paths report the same unit id. Keep the most useful
    // reading of each device: online with a battery value, then online, asleep, needs access, error.
    private static List<DeviceReading> Deduplicate(List<DeviceReading> readings) =>
        readings
            .GroupBy(r => r.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(Rank).First())
            .ToList();

    private static int Rank(DeviceReading reading) => reading.Status switch
    {
        DeviceStatus.Online when reading.HasBattery => 0,
        DeviceStatus.Online => 1,
        DeviceStatus.Asleep => 2,
        DeviceStatus.NeedsAccess => 3,
        _ => 4,
    };

    private List<DeviceReading> PollChannel(HidppCollections collections, CancellationToken ct)
    {
        HidppChannel channel;
        try
        {
            channel = Open(collections);
        }
        catch (HidIoException e)
        {
            return OpenFailed(collections, e);
        }

        var readings = new List<DeviceReading>();
        var poll = new ChannelPoll(channel, collections);
        channel.Unsolicited += report => OnUnsolicited(poll, report);
        using (channel)
        {
            try
            {
                if (LogitechModels.IsWired(collections.Short.ProductId)) ReadWired(poll, readings, ct);
                else ReadReceiver(poll, readings, ct);
            }
            catch (HidIoException e) when (e.DeviceGone)
            {
                Log.Info($"Logitech {collections.Short.Label} went away during the poll: {e.Message}");
            }
            catch (HidIoException e)
            {
                Log.Warn($"Logitech {collections.Short.Label}: {e.Message}");
                readings.Add(ChannelReading(collections, DeviceStatus.Error, $"I/O error (Win32 error {e.Win32Error})"));
            }
            catch (UnsafeCommandException e)
            {
                Log.Error($"Logitech {collections.Short.Label}: a frame was refused by the allow-list", e);
                readings.Add(ChannelReading(collections, DeviceStatus.Error, "Internal error: unsafe command refused"));
            }
        }

        foreach (var index in poll.Reconnected)
        {
            if (_devices.TryGetValue((collections.Key, index), out var known)) ForgetFeatures(known);
        }
        return readings;
    }

    // Seen while waiting for our replies: a reconnect means the device may have new firmware and feature indexes.
    private void OnUnsolicited(ChannelPoll poll, byte[] report)
    {
        if (HidppProtocol.TryParseConnectionNotification(report, out var index, out var linkEstablished))
        {
            Log.Info($"Logitech device {index}: link {(linkEstablished ? "established" : "lost")}");
            poll.Reconnected.Add(index);
        }
        else if (HidppProtocol.TryParseFeatureEvent(report, out index, out _, out _)
            && _devices.TryGetValue((poll.Collections.Key, index), out var known)
            && known.Features?.GetValueOrDefault(HidppFeature.WirelessDeviceStatus) is { } wirelessIndex
            && HidppProtocol.IsReconnectEvent(report, wirelessIndex))
        {
            Log.Info($"Logitech device {index}: reconnected");
            poll.Reconnected.Add(index);
        }
    }

    private void ForgetAbsentChannels(IReadOnlyList<HidppCollections> present)
    {
        var keys = present.Select(c => c.Key).ToHashSet();
        foreach (var key in _devices.Keys.Where(k => !keys.Contains(k.Channel)).ToList())
        {
            _devices.Remove(key);
            _policy.ForgetChannel(key.Channel);
        }
    }

    // The cached device at this index, or a new one when the receiver reports a different device there.
    private KnownDevice Remember(HidppCollections collections, byte index, SlotIdentity identity)
    {
        if (_devices.TryGetValue((collections.Key, index), out var known))
        {
            if (known.Identity == identity) return known;
            Log.Info($"Logitech device {index}: another device is paired in this slot now");
            ForgetFeatures(known);
        }
        known = new KnownDevice(collections.Key, collections.Short.ProductId, index, identity);
        _devices[(collections.Key, index)] = known;
        return known;
    }

    private void Forget(KnownDevice known)
    {
        ForgetFeatures(known);
        _devices.Remove((known.Channel, known.Index));
    }

    private void ForgetFeatures(KnownDevice known)
    {
        known.Features = null;
        known.BatteryFeature = null;
        known.Capabilities = null;
        _policy.ForgetDevice(known.Channel, known.Index);
    }

    private List<KnownDevice> KnownOn(HidppCollections collections) =>
        _devices.Values.Where(d => d.Channel == collections.Key).OrderBy(d => d.Index).ToList();

    private HidppChannel Open(HidppCollections collections)
    {
        var shortLink = new GuardedHidConnection(_hid.Open(collections.Short, HidAccess.ReadWrite), _policy);
        try
        {
            var longLink = new GuardedHidConnection(_hid.Open(collections.Long, HidAccess.ReadWrite), _policy);
            return new HidppChannel(shortLink, longLink, _timings.Slice);
        }
        catch
        {
            shortLink.Dispose();
            throw;
        }
    }

    private List<DeviceReading> OpenFailed(HidppCollections collections, HidIoException e)
    {
        if (e.DeviceGone) return [];
        Log.Warn($"Cannot open Logitech {collections.Short.Label}: {e.Message}");
        var (status, detail) = e.Win32Error == HidIoException.AccessDenied
            ? (DeviceStatus.NeedsAccess, "Windows denied access to the receiver")
            : (DeviceStatus.Error, $"Cannot open the receiver (Win32 error {e.Win32Error})");
        var known = KnownOn(collections).Select(k => Reading(k, status, GenericSource, detail: detail)).ToList();
        return known.Count > 0 ? known : [ChannelReading(collections, status, detail)];
    }

    // B5/03 for the slot count, then per slot B5/2n (empty slot: 0x8F error) and B5/3n for the device serial.
    private void ReadReceiver(ChannelPoll poll, List<DeviceReading> readings, CancellationToken ct)
    {
        var info = poll.Channel.Send(HidppProtocol.ReadReceiverInfo(0x03), _timings.Register);
        if (info.TimedOut)
        {
            Log.Warn($"Logitech {poll.Collections.Short.Label}: the receiver does not answer register reads");
            readings.AddRange(KnownOn(poll.Collections).Select(k => Reading(k, DeviceStatus.Asleep, GenericSource, detail: "Receiver not responding")));
            return;
        }

        uint? receiverSerial = null;
        var slots = MaxSlots;
        if (info.Kind == HidppMatchKind.Reply)
        {
            var parsed = HidppProtocol.ParseReceiverInfo(info.Report);
            receiverSerial = parsed.Serial;
            if (parsed.MaxDevices is >= 1 and <= MaxSlots) slots = parsed.MaxDevices;
        }

        for (var slot = 1; slot <= slots && !poll.Gone; slot++)
        {
            ct.ThrowIfCancellationRequested();
            var pairing = poll.Channel.Send(HidppProtocol.ReadReceiverInfo((byte)(0x20 + slot - 1)), _timings.Register);
            if (pairing.Kind != HidppMatchKind.Reply)
            {
                if (_devices.TryGetValue((poll.Collections.Key, (byte)slot), out var unpaired)) Forget(unpaired);
                continue;
            }
            var pairingInfo = HidppProtocol.ParsePairingInfo(pairing.Report);

            var extended = poll.Channel.Send(HidppProtocol.ReadReceiverInfo((byte)(0x30 + slot - 1)), _timings.Register);
            uint? serial = extended.Kind == HidppMatchKind.Reply ? HidppProtocol.ParseExtendedPairingSerial(extended.Report) : null;

            var identity = new SlotIdentity(receiverSerial, serial, pairingInfo.Wpid, pairingInfo.Kind);
            if (ReadDevice(poll, Remember(poll.Collections, (byte)slot, identity), ct) is { } reading) readings.Add(reading);
        }
    }

    // A device on its own cable answers at device index 0xFF; there are no receiver registers to read.
    private void ReadWired(ChannelPoll poll, List<DeviceReading> readings, CancellationToken ct)
    {
        var known = Remember(poll.Collections, HidppProtocol.ReceiverIndex, WiredIdentity);
        known.UsbPid ??= poll.Collections.Short.ProductId;
        if (ReadDevice(poll, known, ct) is { } reading) readings.Add(reading);
    }

    private DeviceReading? ReadDevice(ChannelPoll poll, KnownDevice known, CancellationToken ct)
    {
        try
        {
            var ping = poll.Channel.Send(HidppProtocol.Ping(known.Index, SoftwareId, NextMark()), _timings.Ping);
            switch (ping.Kind)
            {
                case HidppMatchKind.None:
                    return Asleep(poll, known, NotResponding);
                case HidppMatchKind.Hidpp10Error when ping.ErrorCode == HidppProtocol.ErrorUnknownDevice:
                    Forget(known);
                    return null;
                case HidppMatchKind.Hidpp10Error or HidppMatchKind.Hidpp20Error:
                    return Asleep(poll, known, NotResponding);
            }

            var protocol = HidppProtocol.ParsePingReply(ping.Report);
            if (known.Protocol is { } previous && previous != protocol) ForgetFeatures(known);
            known.Protocol = protocol;
            ct.ThrowIfCancellationRequested();
            return ReadHidpp20(poll, known, ct);
        }
        catch (HidppFailure f) when (f.Kind == FailureKind.NotResponding)
        {
            return Asleep(poll, known, "Stopped responding (asleep or off)");
        }
        catch (HidppFailure f)
        {
            if (f.Kind == FailureKind.StaleIndex) ForgetFeatures(known);
            return Reading(known, DeviceStatus.Error, GenericSource, detail: f.Message);
        }
        catch (UnsafeCommandException e)
        {
            Log.Error($"Logitech device {known.Index}: a frame was refused by the allow-list", e);
            ForgetFeatures(known);
            return Reading(known, DeviceStatus.Error, GenericSource, detail: "Internal error: unsafe command refused");
        }
        catch (HidIoException e) when (e.DeviceGone)
        {
            poll.Gone = true;
            return Asleep(poll, known, "Disconnected during the read");
        }
        catch (HidIoException e)
        {
            Log.Warn($"Logitech device {known.Index}: {e.Message}");
            return Reading(known, DeviceStatus.Error, GenericSource, detail: $"I/O error (Win32 error {e.Win32Error})");
        }
    }

    // Features from the cache (after checking the battery index still holds the same feature) or resolved now. Error 6
    // drops the cache and retries once.
    private DeviceReading ReadHidpp20(ChannelPoll poll, KnownDevice known, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (known.Features is not null && !BatteryIndexUnchanged(poll, known))
                {
                    Log.Info($"Logitech device {known.Index}: battery feature index moved; resolving features again");
                    ForgetFeatures(known);
                }
                if (known.Features is null) Resolve(poll, known, ct);
                ct.ThrowIfCancellationRequested();
                return ReadBattery(poll, known);
            }
            catch (HidppFailure f) when (f.Kind == FailureKind.StaleIndex && attempt == 0)
            {
                Log.Info($"Logitech device {known.Index}: stale feature index; resolving features again");
                ForgetFeatures(known);
            }
        }
    }

    private bool BatteryIndexUnchanged(ChannelPoll poll, KnownDevice known)
    {
        if (known.BatteryFeature is not { } feature) return true;
        var reply = Optional(() => Call(poll, HidppProtocol.GetFeature(known.Index, feature, SoftwareId)));
        return reply is not null && HidppProtocol.ParseFeatureInfo(reply).Index == known.Features![feature];
    }

    // IRoot getFeature for the info, name and battery features; registers each index with the policy before any
    // call to it, then reads the unit id, name, kind and battery capabilities.
    private void Resolve(ChannelPoll poll, KnownDevice known, CancellationToken ct)
    {
        _policy.ForgetDevice(known.Channel, known.Index);
        var features = new Dictionary<ushort, byte>();

        bool Lookup(ushort featureId)
        {
            ct.ThrowIfCancellationRequested();
            var reply = Optional(() => Call(poll, HidppProtocol.GetFeature(known.Index, featureId, SoftwareId)));
            var index = reply is null ? (byte)0 : HidppProtocol.ParseFeatureInfo(reply).Index;
            if (index == 0) return false;
            if (index >= 0x80 || features.ContainsValue(index))
            {
                Log.Warn($"Logitech device {known.Index}: feature 0x{featureId:X4} reported at index 0x{index:X2}, which is invalid or taken; ignoring it");
                return false;
            }
            _policy.RegisterFeature(poll.Collections.Short, known.Index, index, featureId);
            features[featureId] = index;
            return true;
        }

        Lookup(HidppFeature.DeviceFwInfo);
        Lookup(HidppFeature.DeviceName);
        Lookup(HidppFeature.WirelessDeviceStatus);
        ushort? battery = null;
        foreach (var feature in BatteryFeatures)
        {
            if (!Lookup(feature)) continue;
            battery = feature;
            break;
        }

        if (features.TryGetValue(HidppFeature.DeviceFwInfo, out var fwIndex)
            && Optional(() => Call(poll, Request(known, fwIndex, 0))) is { } info)
        {
            var ids = HidppProtocol.ParseDeviceIds(info);
            if (ids.UnitId != 0) known.UnitId = ids.UnitId;
            known.UsbPid = ids.UsbPid ?? known.UsbPid;
        }
        if (features.TryGetValue(HidppFeature.DeviceName, out var nameIndex))
        {
            known.Name = ReadName(poll, known, nameIndex) ?? known.Name;
            if (Optional(() => Call(poll, Request(known, nameIndex, 2))) is { } type)
                known.Kind = HidppProtocol.KindFromDeviceType(HidppProtocol.ParseDeviceType(type));
        }
        known.Capabilities = battery == HidppFeature.UnifiedBattery
            && Optional(() => Call(poll, Request(known, features[HidppFeature.UnifiedBattery], 0))) is { } caps
                ? HidppProtocol.ParseUnifiedCapabilities(caps)
                : null;

        // Cached only once everything above answered, so a device that stops mid-way is resolved again next time.
        known.BatteryFeature = battery;
        known.Features = features;
        Log.Info($"Logitech device {known.Index} ({known.Name ?? "unnamed"}): HID++ {known.Protocol?.Major}.{known.Protocol?.Minor}, features "
            + string.Join(", ", features.Select(f => $"{f.Key:X4}@{f.Value:X2}")));
    }

    private string? ReadName(ChannelPoll poll, KnownDevice known, byte nameIndex)
    {
        try
        {
            var length = Math.Min(HidppProtocol.ParseNameLength(Call(poll, Request(known, nameIndex, 0))), MaxNameLength);
            var bytes = new List<byte>(length);
            while (bytes.Count < length)
            {
                var chunk = HidppProtocol.NameChunk(Call(poll, Request(known, nameIndex, 1, (byte)bytes.Count)), length - bytes.Count);
                if (chunk.IsEmpty) break;
                bytes.AddRange(chunk);
            }
            var name = HidppProtocol.DecodeName(bytes.ToArray());
            return name.Length > 0 ? name : null;
        }
        catch (HidppFailure f) when (f.Kind == FailureKind.DeviceError)
        {
            return null;
        }
    }

    private DeviceReading ReadBattery(ChannelPoll poll, KnownDevice known)
    {
        if (known.BatteryFeature is not { } feature)
            return Reading(known, DeviceStatus.Online, GenericSource, detail: "No supported battery feature");

        var index = known.Features![feature];
        var battery = feature switch
        {
            HidppFeature.UnifiedBattery => HidppProtocol.DecodeUnifiedBattery(Call(poll, Request(known, index, 1)), known.Capabilities),
            HidppFeature.BatteryStatus => HidppProtocol.DecodeBatteryStatus(Call(poll, Request(known, index, 0))),
            HidppFeature.BatteryVoltage => HidppProtocol.DecodeBatteryVoltage(Call(poll, Request(known, index, 0))),
            _ => HidppProtocol.DecodeAdcMeasurement(Call(poll, Request(known, index, 0))),
        };
        var source = $"HID++ 0x{feature:X4}";
        if (battery is not { } value) return Reading(known, DeviceStatus.Asleep, source, detail: "Inactive (asleep or off)");
        return Reading(known, DeviceStatus.Online, source, [BatteryComponent.Single(value.Percent, value.Charge)], value.Approximate);
    }

    // A HID++ 2.0 call: the reply, or a HidppFailure. BUSY is retried; a 0x8F error from the receiver or no reply
    // means the device cannot be reached right now.
    private byte[] Call(ChannelPoll poll, byte[] request)
    {
        for (var attempt = 0; ; attempt++)
        {
            var result = poll.Channel.Send(request, _timings.Feature);
            switch (result.Kind)
            {
                case HidppMatchKind.Reply:
                    return result.Report!;
                case HidppMatchKind.None:
                    throw new HidppFailure(FailureKind.NotResponding, "No reply");
                case HidppMatchKind.Hidpp10Error:
                    throw new HidppFailure(FailureKind.NotResponding, $"Receiver error 0x{result.ErrorCode:X2}");
                case HidppMatchKind.Hidpp20Error when result.ErrorCode == HidppProtocol.ErrorBusy && attempt < MaxBusyRetries:
                    Thread.Sleep(_timings.BusyRetryDelay);
                    continue;
                case HidppMatchKind.Hidpp20Error when result.ErrorCode == HidppProtocol.ErrorBusy:
                    throw new HidppFailure(FailureKind.NotResponding, "Busy");
                case HidppMatchKind.Hidpp20Error when result.ErrorCode == HidppProtocol.ErrorInvalidFeatureIndex:
                    throw new HidppFailure(FailureKind.StaleIndex, "Feature index no longer valid");
                default:
                    throw new HidppFailure(FailureKind.DeviceError, $"Device error 0x{result.ErrorCode:X2}");
            }
        }
    }

    private static byte[]? Optional(Func<byte[]> call)
    {
        try
        {
            return call();
        }
        catch (HidppFailure f) when (f.Kind == FailureKind.DeviceError)
        {
            return null;
        }
    }

    private static byte[] Request(KnownDevice known, byte featureIndex, int function, byte p0 = 0) =>
        HidppProtocol.FeatureRequest(known.Index, featureIndex, function, SoftwareId, p0);

    // An asleep reading needs a stable id (unit id or the receiver's serial for the slot); without one it is skipped
    // rather than shown under an id the device will not have once it answers.
    private DeviceReading? Asleep(ChannelPoll poll, KnownDevice known, string detail)
    {
        if (known.UnitId is null && known.Identity.DeviceSerial is null) return null;
        if (known.Name is null && known.Codename is null && Model(known) is null) known.Codename = TryReadCodename(poll, known);
        return Reading(known, DeviceStatus.Asleep, GenericSource, detail: detail);
    }

    // B5/4n, best effort: the codename names a device that is not answering and is not in the model table.
    private string? TryReadCodename(ChannelPoll poll, KnownDevice known)
    {
        if (poll.Gone || known.Index is < 1 or > MaxSlots) return null;
        try
        {
            var reply = poll.Channel.Send(HidppProtocol.ReadReceiverInfo((byte)(0x40 + known.Index - 1)), _timings.Register);
            return reply.Kind == HidppMatchKind.Reply && HidppProtocol.ParseCodename(reply.Report) is { Length: > 0 } codename ? codename : null;
        }
        catch (HidIoException e)
        {
            poll.Gone |= e.DeviceGone;
            return null;
        }
    }

    private DeviceReading Reading(KnownDevice known, DeviceStatus status, string source,
        IReadOnlyList<BatteryComponent>? components = null, bool approximate = false, string? detail = null)
    {
        var model = Model(known);
        var id = known.UnitId ?? known.Identity.DeviceSerial;
        return new DeviceReading(
            id is { } unit ? $"logitech:{unit:X8}" : $"logitech:usb-{known.ProductId:X4}-{known.Index:X2}",
            VendorName,
            known.Name ?? model?.Name ?? known.Codename ?? "Logitech device",
            known.Kind ?? model?.Kind ?? HidppProtocol.KindFromPairingKind(known.Identity.PairingKind),
            status,
            components ?? [],
            _time.GetUtcNow(),
            source,
            approximate,
            detail);
    }

    // A reading for a receiver that could not be read at all.
    private DeviceReading ChannelReading(HidppCollections collections, DeviceStatus status, string detail)
    {
        var model = LogitechModels.Find(null, collections.Short.ProductId);
        return new DeviceReading($"logitech:usb-{collections.Short.ProductId:X4}", VendorName, model?.Name ?? "Logitech receiver",
            model?.Kind ?? DeviceKind.Other, status, [], _time.GetUtcNow(), GenericSource, Detail: detail);
    }

    private static (string Name, DeviceKind Kind)? Model(KnownDevice known) => LogitechModels.Find(known.Identity.Wpid, known.UsbPid);

    private static byte NextMark() => (byte)Random.Shared.Next(1, 256);

    private enum FailureKind
    {
        /// <summary>No reply, a receiver 0x8F error, or BUSY too often: the device cannot be reached now.</summary>
        NotResponding,

        /// <summary>HID++ 2.0 error 6: the cached feature index is no longer valid.</summary>
        StaleIndex,

        /// <summary>Any other HID++ 2.0 error.</summary>
        DeviceError,
    }

    private sealed class HidppFailure(FailureKind kind, string message) : Exception(message)
    {
        public FailureKind Kind { get; } = kind;
    }

    // What the receiver reports for a slot. All null for a device on its own cable.
    private sealed record SlotIdentity(uint? ReceiverSerial, uint? DeviceSerial, ushort? Wpid, byte PairingKind);

    // What is known about the device at one index of one receiver.
    private sealed class KnownDevice(string channel, ushort productId, byte index, SlotIdentity identity)
    {
        public string Channel { get; } = channel;

        /// <summary>Product id of the receiver (or of the device itself on its own cable).</summary>
        public ushort ProductId { get; } = productId;

        public byte Index { get; } = index;

        public SlotIdentity Identity { get; } = identity;

        public (int Major, int Minor)? Protocol { get; set; }

        /// <summary>Feature id to index for the features resolved with IRoot; null until resolved.</summary>
        public Dictionary<ushort, byte>? Features { get; set; }

        public ushort? BatteryFeature { get; set; }

        public UnifiedBatteryCapabilities? Capabilities { get; set; }

        public uint? UnitId { get; set; }

        public ushort? UsbPid { get; set; }

        public string? Name { get; set; }

        public string? Codename { get; set; }

        public DeviceKind? Kind { get; set; }
    }

    private sealed class ChannelPoll(HidppChannel channel, HidppCollections collections)
    {
        public HidppChannel Channel { get; } = channel;

        public HidppCollections Collections { get; } = collections;

        /// <summary>The receiver went away during this poll: no more I/O on it.</summary>
        public bool Gone { get; set; }

        /// <summary>Device indexes that reconnected while this poll was running.</summary>
        public HashSet<byte> Reconnected { get; } = [];
    }
}
