using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Vendors.Betop;

/// <summary>Battery and charge state from one getBaseInfo (0x15) reply.</summary>
public readonly record struct BetopStatus(int Percent, ChargeState Charge);

/// <summary>Frames of the Betop gen-4 vendor collection (docs/protocols/betop.md). Pure: no I/O.</summary>
public static class BetopProtocol
{
    public const byte ReportId = 0x02;
    public const int ReportLength = 64;

    /// <summary>getBaseInfo: battery and charge state. Relayed to the pad, so the value is live.</summary>
    public const byte GetBaseInfo = 0x15;

    /// <summary>getFWInfo: firmware of the dongle and fw_conn. Answered by the dongle itself.</summary>
    public const byte GetFirmwareInfo = 0x55;

    /// <summary>getRemoteInfo: firmware of the pad.</summary>
    public const byte GetRemoteInfo = 0x18;

    public const ushort VendorUsagePage = 0x00FF;
    public const ushort VendorUsage = 0x0003;

    private const int PercentOffset = 2;
    private const int ChargeOffset = 4;

    /// <summary>[02 command 00 x62], the full output report.</summary>
    public static byte[] BuildRequest(byte command)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = command;
        return report;
    }

    public static byte[] BuildBatteryRequest() => BuildRequest(GetBaseInfo);

    /// <summary>The collection that carries the protocol: UP 0x00FF (not 0xFF00), U 0x0003, 64-byte input and output
    /// reports. Matched on caps, never on the COLxx index. The VID check is separate (see <see cref="BetopModels"/>).</summary>
    public static bool IsVendorCollection(HidDeviceInfo device) =>
        device is { Accessible: true, UsagePage: VendorUsagePage, Usage: VendorUsage, InputReportLength: ReportLength, OutputReportLength: ReportLength };

    /// <summary>Parses a 02 15 reply. Any other frame (BetopGame's 0x25/0x11/0x21 replies, an unsolicited 0x55, other
    /// report ids) returns false so callers can skip it.</summary>
    /// <param name="chargeInLowNibble">KP50/KP70/PG01: charge state is byte 4 &amp; 0x0F. KP20/KP40: the whole byte.</param>
    public static bool TryParseBattery(ReadOnlySpan<byte> report, bool chargeInLowNibble, out BetopStatus status)
    {
        status = default;
        if (report.Length <= ChargeOffset || report[0] != ReportId || report[1] != GetBaseInfo) return false;

        var charge = chargeInLowNibble ? report[ChargeOffset] & 0x0F : report[ChargeOffset];
        status = new BetopStatus(Math.Min((int)report[PercentOffset], 100), charge switch
        {
            0 => ChargeState.Discharging,
            1 => ChargeState.Charging,
            _ => ChargeState.Unknown, // never observed; BetopGame only knows 1 = charging
        });
        return true;
    }
}
