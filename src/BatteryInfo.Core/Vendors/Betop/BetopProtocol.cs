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
    /// report ids) returns false so callers can skip it. The charge state is byte 4 &amp; 0x0F for every model in
    /// <see cref="BetopModels"/>: only the byte-substituting scripts (kp20/kp30/kp40/kp401/kp40v1r) compare the whole
    /// byte, and those models are not in the table.</summary>
    public static bool TryParseBattery(ReadOnlySpan<byte> report, out BetopStatus status)
    {
        status = default;
        if (report.Length <= ChargeOffset || report[0] != ReportId || report[1] != GetBaseInfo) return false;

        status = new BetopStatus(Math.Min((int)report[PercentOffset], 100), (report[ChargeOffset] & 0x0F) switch
        {
            0 => ChargeState.Discharging,
            1 => ChargeState.Charging,
            _ => ChargeState.Unknown, // never observed; BetopGame only knows 1 = charging
        });
        return true;
    }
}
