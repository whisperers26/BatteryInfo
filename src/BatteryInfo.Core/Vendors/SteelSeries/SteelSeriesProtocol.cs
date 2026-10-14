using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Vendors.SteelSeries;

/// <summary>What one 0xB0 status reply says. <see cref="Percent"/> and <see cref="Charge"/> are stale when the
/// headset is not <see cref="Connected"/>.</summary>
public readonly record struct SteelSeriesStatus(bool Connected, int Percent, ChargeState Charge);

/// <summary>Frames of the Arctis Nova 5 base station's status collection (docs/protocols/steelseries.md). Pure: no I/O.</summary>
public static class SteelSeriesProtocol
{
    public const byte ReportId = 0x00;
    public const int ReportLength = 65;

    /// <summary>The status query: link state, battery, charging, ChatMix.</summary>
    public const byte GetStatus = 0xB0;

    public const ushort StatusUsagePage = 0xFFC0;
    public const ushort StatusUsage = 0x0001;

    private const int LinkOffset = 2;
    private const int PercentOffset = 4;
    private const int ChargeOffset = 5;
    private const byte HeadsetOffline = 0x02;
    private const byte HeadsetCharging = 0x01;

    /// <summary>[00 B0 00 x63], the full output report.</summary>
    public static byte[] BuildStatusRequest()
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = GetStatus;
        return report;
    }

    /// <summary>The collection that carries the protocol: UP 0xFFC0, U 0x0001, 65-byte input and output reports.
    /// Matched on caps, never on the COLxx index.</summary>
    public static bool IsStatusCollection(HidDeviceInfo device) =>
        device is { Accessible: true, UsagePage: StatusUsagePage, Usage: StatusUsage, InputReportLength: ReportLength, OutputReportLength: ReportLength };

    /// <summary>Parses a 00 B0 reply. Any other frame returns false so callers can skip it.</summary>
    public static bool TryParseStatus(ReadOnlySpan<byte> report, out SteelSeriesStatus status)
    {
        status = default;
        if (report.Length <= ChargeOffset || report[0] != ReportId || report[1] != GetStatus) return false;

        status = new SteelSeriesStatus(
            report[LinkOffset] != HeadsetOffline,
            Math.Min((int)report[PercentOffset], 100),
            report[ChargeOffset] == HeadsetCharging ? ChargeState.Charging : ChargeState.Discharging);
        return true;
    }
}
