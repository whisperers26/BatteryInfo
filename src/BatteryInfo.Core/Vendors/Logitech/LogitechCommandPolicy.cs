using System.Text.RegularExpressions;
using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.Logitech;

/// <summary>The Logitech allow-list (docs/protocols/logitech.md §11): receiver register reads (0x81/0x83 of known
/// registers only), IRoot fn0/fn1, and the read functions of a few features. HID++ 2.0 calls are checked by the
/// feature id behind the index, never by the index itself, so a feature index nobody resolved is refused.</summary>
public sealed partial class LogitechCommandPolicy : ICommandPolicy
{
    private const ushort LogitechVendorId = 0x046D;
    private const ushort HidppUsagePage = 0xFF00;

    private readonly object _gate = new();
    private readonly Dictionary<(string Channel, byte Device, byte Index), ushort> _features = [];

    /// <summary>Records that <paramref name="featureIndex"/> of one device is <paramref name="featureId"/>, as resolved
    /// with IRoot getFeature. Any collection of the receiver (or wired device) identifies it.</summary>
    public void RegisterFeature(HidDeviceInfo collection, byte deviceIndex, byte featureIndex, ushort featureId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(featureIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(featureIndex, (byte)0x7F);
        var key = (ChannelKey(collection), deviceIndex, featureIndex);
        lock (_gate)
        {
            if (_features.TryGetValue(key, out var existing) && existing != featureId)
                throw new InvalidOperationException(
                    $"Feature index 0x{featureIndex:X2} of device {deviceIndex} is 0x{existing:X4}; forget the device before re-resolving it.");
            _features[key] = featureId;
        }
    }

    /// <summary>Drops every feature index registered for one device (it reconnected or reported a stale index).</summary>
    public void ForgetDevice(HidDeviceInfo collection, byte deviceIndex) => ForgetDevice(ChannelKey(collection), deviceIndex);

    internal void ForgetDevice(string channelKey, byte deviceIndex)
    {
        lock (_gate)
        {
            foreach (var key in _features.Keys.Where(k => k.Channel == channelKey && k.Device == deviceIndex).ToList())
                _features.Remove(key);
        }
    }

    internal void ForgetChannel(string channelKey)
    {
        lock (_gate)
        {
            foreach (var key in _features.Keys.Where(k => k.Channel == channelKey).ToList()) _features.Remove(key);
        }
    }

    public bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing)
    {
        if (device.VendorId != LogitechVendorId || device.UsagePage != HidppUsagePage) return false;

        var isShort = outgoing.Length == HidppProtocol.ShortLength && outgoing[0] == HidppProtocol.ShortReportId;
        var isLong = outgoing.Length == HidppProtocol.LongLength && outgoing[0] == HidppProtocol.LongReportId;
        if (!isShort && !isLong) return false;

        var deviceIndex = outgoing[1];
        if (deviceIndex is not (>= 1 and <= 6 or HidppProtocol.ReceiverIndex)) return false;

        var subId = outgoing[2];
        if (subId == HidppProtocol.GetRegister) return isShort && IsAllowedRegister(deviceIndex, outgoing[3]);
        if (subId == HidppProtocol.GetLongRegister)
            return isShort && deviceIndex == HidppProtocol.ReceiverIndex && outgoing[3] == HidppProtocol.ReceiverInfoRegister
                && IsAllowedInfoSubRegister(outgoing[4]);
        if (subId >= 0x80) return false;

        var function = outgoing[3] >> 4;
        if ((outgoing[3] & 0x0F) == 0) return false;

        ushort featureId;
        if (subId == 0)
        {
            featureId = HidppFeature.Root;
        }
        else
        {
            lock (_gate)
            {
                if (!_features.TryGetValue((ChannelKey(device), deviceIndex, subId), out featureId)) return false;
            }
        }
        return (AllowedFunctions(featureId) & 1 << function) != 0;
    }

    /// <summary>Identifies the receiver or wired device a collection belongs to: its parent devnode, which the short
    /// and long HID++ collections share, else the path without the collection number.</summary>
    internal static string ChannelKey(HidDeviceInfo device) =>
        string.IsNullOrEmpty(device.ParentInstanceId)
            ? CollectionSuffixRegex().Replace(device.Path, "").ToUpperInvariant()
            : device.ParentInstanceId.ToUpperInvariant();

    // 0x81 reads: receiver notification flags, connection state and firmware; HID++ 1.0 battery registers.
    private static bool IsAllowedRegister(byte deviceIndex, byte register) =>
        register is 0x07 or 0x0D || (deviceIndex == HidppProtocol.ReceiverIndex && register is 0x00 or 0x02 or 0xF1);

    // B5 sub-registers: 03 receiver info, 2n pairing info, 3n extended pairing, 4n codename (n = slot - 1).
    private static bool IsAllowedInfoSubRegister(byte subRegister) =>
        subRegister == 0x03 || ((subRegister & 0x0F) <= 5 && (subRegister >> 4) is 2 or 3 or 4);

    /// <summary>Bit n set: function n of the feature is a read this app may send.</summary>
    private static int AllowedFunctions(ushort featureId) => featureId switch
    {
        HidppFeature.Root or HidppFeature.FeatureSet or HidppFeature.DeviceFwInfo
            or HidppFeature.BatteryStatus or HidppFeature.UnifiedBattery => 0b011,
        HidppFeature.DeviceName => 0b111,
        HidppFeature.BatteryVoltage or HidppFeature.AdcMeasurement => 0b001,
        _ => 0,
    };

    // "&col01" and the instance suffix "&0000" before the interface class GUID.
    [GeneratedRegex(@"&col[0-9a-f]+|&[0-9a-f]{4}(?=#\{)", RegexOptions.IgnoreCase)]
    private static partial Regex CollectionSuffixRegex();
}
