using System.Text;

namespace BatteryInfo.Core.Vendors.Rog;

/// <summary>One ROG GET command: the header bytes after the report id. Every ROG request this app sends is
/// [reportId][Command][Sub][Index lo][Index hi] followed by zeros.</summary>
public readonly record struct RogCommand(byte Command, byte Sub, ushort Index = 0)
{
    public override string ToString() => $"{Command:X2} {Sub:X2} {Index & 0xFF:X2} {Index >> 8:X2}";
}

/// <summary>A paired device listed by the receiver's A0 00 scan.</summary>
/// <param name="ControlReportId">Output report id of the receiver collection that reaches it (2 keyboard, 3 mouse).</param>
/// <param name="NotifyReportId">Input report id of its event collection (never opened by this app).</param>
public readonly record struct RogSlot(ushort ProductId, byte ControlReportId, byte NotifyReportId);

/// <summary>A battery reading. <paramref name="Percent"/> is null when the device reported a value above 100;
/// <paramref name="Charging"/> is null for an undocumented flag value. <paramref name="Asleep"/> is the mouse
/// "0 % and not charging" reply, which means no data rather than an empty battery.</summary>
public readonly record struct RogPower(int? Percent, bool? Charging, bool Asleep = false);

public readonly record struct RogCetraBattery(int? Left, int? Right, int? Case);

public readonly record struct RogCetraCharging(bool Left, bool Right, bool Case);

public enum RogConnectionType : byte
{
    Wired = 0,
    Rf = 1,
    Bluetooth = 2,
    NotPresent = 0xFF,
}

public enum RogFrame
{
    /// <summary>Not an answer to this request (another command, another collection, foreign software).</summary>
    Unrelated,

    /// <summary>The answer to this request.</summary>
    Reply,

    /// <summary>FF AA: unsupported command, empty slot, or a device that is asleep or out of reach.</summary>
    Rejected,
}

/// <summary>Pure builders and parsers for the ROG (OMNI receiver, Azoth X, Cetra) vendor protocol, see
/// docs/protocols/rog.md. In a reply, d[n] is report[n + 1].</summary>
public static class RogProtocol
{
    public const int HeaderLength = 5;
    public const byte CetraReportId = 0xCC;

    // Receiver (OMNI report id 1, usage page FF02).
    public static readonly RogCommand ReceiverFirmwareVersion = new(0xA1, 0x01);
    public static readonly RogCommand ReceiverSerialNumber = new(0xA1, 0x00);
    public static readonly RogCommand ScanForPairedDevices = new(0xA0, 0x00);

    // Keyboard and mouse control collections (usage page FF00 / FF01).
    public static readonly RogCommand DeviceInfo = new(0x12, 0x00, 0);
    public static readonly RogCommand KeyboardPowerInfo = new(0x12, 0x01);
    public static readonly RogCommand KeyboardConnectionType = new(0x12, 0x03);
    public static readonly RogCommand MouseExistInfo = new(0x12, 0x00, 2);
    public static readonly RogCommand MousePowerInfo = new(0x12, 0x07);

    // Cetra (report id 0xCC): 12 <key> 00 00.
    public static readonly RogCommand CetraDeviceInfo = new(0x12, 0x00);
    public static readonly RogCommand CetraConnectionStatus = new(0x12, 0x01);
    public static readonly RogCommand CetraProtocolVersion = new(0x12, 0x07);
    public static readonly RogCommand CetraChargingStatus = new(0x12, 0x11);
    public static readonly RogCommand CetraBatteryLevel = new(0x12, 0x12);
    public static readonly RogCommand CetraPowerSavingStatus = new(0x12, 0x13);

    /// <summary>A full output report: [reportId][cmd][sub][idx lo][idx hi], then zeros up to <paramref name="length"/>
    /// (the collection's OutputReportLength: 64 on the OMNI, 65 for a report-id-0 collection).</summary>
    public static byte[] BuildRequest(int length, byte reportId, RogCommand command)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, HeaderLength);
        var request = new byte[length];
        request[0] = reportId;
        request[1] = command.Command;
        request[2] = command.Sub;
        request[3] = (byte)command.Index;
        request[4] = (byte)(command.Index >> 8);
        return request;
    }

    /// <summary>Whether <paramref name="report"/>, read from the collection whose report id is <paramref name="reportId"/>,
    /// answers <paramref name="command"/>. Replies echo the whole header; the A0 00 scan echoes only cmd and sub.</summary>
    public static RogFrame Classify(ReadOnlySpan<byte> report, byte reportId, RogCommand command)
    {
        if (report.Length < 3 || report[0] != reportId) return RogFrame.Unrelated;
        if (report[1] == 0xFF && report[2] == 0xAA) return RogFrame.Rejected;
        if (report[1] != command.Command || report[2] != command.Sub) return RogFrame.Unrelated;
        if (command == ScanForPairedDevices) return RogFrame.Reply;
        return report.Length >= HeaderLength && report[3] == (byte)command.Index && report[4] == (byte)(command.Index >> 8)
            ? RogFrame.Reply
            : RogFrame.Unrelated;
    }

    /// <summary>A0 00 reply: count = d[2] | d[3] &lt;&lt; 8, then 4 bytes per slot from d[4]: PID LE, control rid, notify rid.</summary>
    public static IReadOnlyList<RogSlot> ParseScan(ReadOnlySpan<byte> report)
    {
        var d = Data(report);
        if (d.Length < 4) return [];
        var count = d[2] | d[3] << 8;
        var slots = new List<RogSlot>();
        for (var i = 0; i < count; i++)
        {
            var e = 4 + 4 * i;
            if (e + 4 > d.Length) break;
            slots.Add(new RogSlot((ushort)(d[e] | d[e + 1] << 8), d[e + 2], d[e + 3]));
        }
        return slots;
    }

    /// <summary>A1 00 reply: ASCII serial in d[4..15], NULs stripped. Null when blank or not printable.</summary>
    public static string? ParseSerial(ReadOnlySpan<byte> report)
    {
        var d = Data(report);
        if (d.Length <= 4) return null;
        var field = d[4..Math.Min(d.Length, 16)];
        var text = new StringBuilder();
        foreach (var b in field)
        {
            if (b == 0) continue;
            if (b is < 0x21 or > 0x7E) return null;
            text.Append((char)b);
        }
        return text.Length == 0 ? null : text.ToString();
    }

    /// <summary>12 03 reply: d[4] = 0 wired, 1 RF, 2 Bluetooth, 0xFF not present.</summary>
    public static RogConnectionType? ParseConnectionType(ReadOnlySpan<byte> report)
    {
        var d = Data(report);
        return d.Length > 4 ? (RogConnectionType)d[4] : null;
    }

    /// <summary>Keyboard 12 01 reply (at least 10 data bytes): d[5] = percent, d[8] = 1 when charging.</summary>
    public static RogPower? ParseKeyboardPower(ReadOnlySpan<byte> report)
    {
        var d = Data(report);
        if (d.Length < 10) return null;
        bool? charging = d[8] switch { 0 => false, 1 => true, _ => null };
        return new RogPower(Percent(d[5]), charging);
    }

    /// <summary>Mouse 12 00 02 reply: d[4] = 1 when the mouse is awake and linked.</summary>
    public static bool? ParseMouseExists(ReadOnlySpan<byte> report)
    {
        var d = Data(report);
        return d.Length > 4 ? d[4] == 1 : null;
    }

    /// <summary>Mouse 12 07 reply (at least 11 data bytes): d[4] = percent, d[9] &gt; 0 when charging. d[4] = 0 with
    /// d[9] = 0 means asleep / no data.</summary>
    public static RogPower? ParseMousePower(ReadOnlySpan<byte> report)
    {
        var d = Data(report);
        if (d.Length < 11) return null;
        if (d[4] == 0 && d[9] == 0) return new RogPower(null, null, Asleep: true);
        return new RogPower(Percent(d[4]), d[9] > 0);
    }

    /// <summary>Cetra 12 12 reply: d[4] left, d[5] right, d[6] case; above 100 means unknown.</summary>
    public static RogCetraBattery? ParseCetraBattery(ReadOnlySpan<byte> report)
    {
        var d = Data(report);
        return d.Length > 6 ? new RogCetraBattery(Percent(d[4]), Percent(d[5]), Percent(d[6])) : null;
    }

    /// <summary>Cetra 12 11 reply: d[4] = 1 left, 2 right, 3 both charging; d[5] = 1 case charging.</summary>
    public static RogCetraCharging? ParseCetraCharging(ReadOnlySpan<byte> report)
    {
        var d = Data(report);
        return d.Length > 5 ? new RogCetraCharging(d[4] is 1 or 3, d[4] is 2 or 3, d[5] == 1) : null;
    }

    private static ReadOnlySpan<byte> Data(ReadOnlySpan<byte> report) => report.Length > 1 ? report[1..] : [];

    private static int? Percent(byte value) => value <= 100 ? value : null;
}
