using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.SteelSeries;

/// <summary>The SteelSeries allow-list: the status query 00 B0 with an all-zero payload, sent only to the status
/// collection of a known base station. Everything else is refused, including save (09, 35), sidetone (39), mic volume
/// (37), LED (AE), auto power-off (A3), volume limiter (27) and equalizer (33).</summary>
public sealed class SteelSeriesCommandPolicy : ICommandPolicy
{
    public bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing)
    {
        if (SteelSeriesModels.Find(device.VendorId, device.ProductId) is null) return false;
        if (!SteelSeriesProtocol.IsStatusCollection(device)) return false;

        if (outgoing.Length != SteelSeriesProtocol.ReportLength || outgoing[0] != SteelSeriesProtocol.ReportId) return false;
        if (outgoing[1] != SteelSeriesProtocol.GetStatus) return false;
        return !outgoing[2..].ContainsAnyExcept((byte)0);
    }
}
