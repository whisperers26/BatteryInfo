using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.Razer;

/// <summary>The only Razer frames this app may send (spec §7, razer.md §4): Get Battery Level 07/80 and Get
/// Charging Status 07/84 (size 2, battery id 0), and Get Wireless Connection Status 00/BF (size 0x50) to a
/// dongle. Only to a known model's USB PIDs, as a complete well-formed 91-byte host request with every argument
/// zero. Everything else, including every SET (id bit 7 clear), is refused.</summary>
public sealed class RazerCommandPolicy : ICommandPolicy
{
    public bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing)
    {
        if (RazerModels.FindUsb(device.VendorId, device.ProductId) is not { } match) return false;
        if (!IsPlainHostRequest(outgoing)) return false;

        var command = (outgoing[RazerProtocol.ClassIndex], outgoing[RazerProtocol.IdIndex], outgoing[RazerProtocol.DataSizeIndex]);
        return command switch
        {
            (RazerProtocol.BatteryClass, RazerProtocol.BatteryLevelId, RazerProtocol.BatteryDataSize) => true,
            (RazerProtocol.BatteryClass, RazerProtocol.ChargingStatusId, RazerProtocol.BatteryDataSize) => true,
            (RazerProtocol.DeviceClass, RazerProtocol.WirelessStatusId, RazerProtocol.WirelessStatusDataSize) =>
                match.Link == RazerLink.Dongle,
            _ => false,
        };
    }

    // Report id 0, status 0, no remaining packets, protocol 0, all 80 arguments zero (battery id 0), valid CRC,
    // reserved byte 0. Any transaction id.
    private static bool IsPlainHostRequest(ReadOnlySpan<byte> frame) =>
        frame.Length == RazerProtocol.BufferLength
        && frame[0] == 0
        && frame[RazerProtocol.StatusIndex] == 0
        && frame[RazerProtocol.RemainingIndex] == 0
        && frame[RazerProtocol.RemainingIndex + 1] == 0
        && frame[RazerProtocol.ProtocolTypeIndex] == 0
        && !frame.Slice(RazerProtocol.ArgumentsIndex, RazerProtocol.ArgumentCount).ContainsAnyExcept((byte)0)
        && RazerProtocol.HasValidCrc(frame)
        && frame[RazerProtocol.ReservedIndex] == 0;
}
