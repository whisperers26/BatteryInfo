using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.Betop;

/// <summary>The Betop allow-list: the read queries 02 15 (battery), 02 55 and 02 18 (firmware), each a 64-byte frame
/// with an all-zero payload, sent only to the vendor collection of a VID 0x20BC plain-byte model. Everything else is
/// refused: firmware update 0x14, mode/sync 0x11/0x21/0x31, profile and MAC commands, and any frame to VID 0x20DD
/// (the KP70C2 dongle while the pad sleeps, also BetopGame's firmware-update target) or 0x0517.</summary>
public sealed class BetopCommandPolicy : ICommandPolicy
{
    public bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing)
    {
        if (device.VendorId != BetopModels.OnlineVendorId) return false;
        if (BetopModels.FindByProductId(device.ProductId) is null) return false;
        if (!BetopProtocol.IsVendorCollection(device)) return false;

        if (outgoing.Length != BetopProtocol.ReportLength || outgoing[0] != BetopProtocol.ReportId) return false;
        if (outgoing[1] is not (BetopProtocol.GetBaseInfo or BetopProtocol.GetFirmwareInfo or BetopProtocol.GetRemoteInfo)) return false;

        // An all-zero payload: 0x27-style headers double as GET and SET, so nothing may ride along.
        return !outgoing[2..].ContainsAnyExcept((byte)0);
    }
}
