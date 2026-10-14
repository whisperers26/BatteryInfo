using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.Rog;

/// <summary>The ROG allow-list (spec §7, docs/protocols/rog.md §9): exact GET tuples only, each on the collection it
/// belongs to, with the collection's own report id and an all-zero payload. Everything else is refused, including
/// A2 00 (pairing mode), A2 01 (unpair), B0 00 (dongle reset), the 0x50/0x51 SET families and Cetra 0x51.</summary>
public sealed class RogCommandPolicy : ICommandPolicy
{
    private const ushort ReceiverControlPage = 0xFF02;
    private const ushort KeyboardControlPage = 0xFF00;
    private const ushort MouseControlPage = 0xFF01;

    private static readonly HashSet<RogCommand> ReceiverCommands =
    [
        RogProtocol.ReceiverFirmwareVersion,
        RogProtocol.ReceiverSerialNumber,
        RogProtocol.ScanForPairedDevices,
    ];

    private static readonly HashSet<RogCommand> DeviceCommands =
    [
        RogProtocol.KeyboardConnectionType,
        RogProtocol.KeyboardPowerInfo,
        RogProtocol.DeviceInfo,
        RogProtocol.MouseExistInfo,
        RogProtocol.MousePowerInfo,
    ];

    private static readonly HashSet<RogCommand> CetraCommands =
    [
        RogProtocol.CetraDeviceInfo,
        RogProtocol.CetraConnectionStatus,
        RogProtocol.CetraProtocolVersion,
        RogProtocol.CetraChargingStatus,
        RogProtocol.CetraBatteryLevel,
        RogProtocol.CetraPowerSavingStatus,
    ];

    public bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing)
    {
        if (!RogModels.IsSupported(device.VendorId, device.ProductId) || device.Usage != 1) return false;
        if (outgoing.Length < RogProtocol.HeaderLength) return false;
        if (outgoing[0] != (device.OutputReportId ?? 0)) return false;
        if (outgoing[RogProtocol.HeaderLength..].ContainsAnyExcept((byte)0)) return false;

        var command = new RogCommand(outgoing[1], outgoing[2], (ushort)(outgoing[3] | outgoing[4] << 8));
        var model = RogModels.Find(device.ProductId)!;

        if (model.Kind == DeviceKind.Earbuds)
        {
            return device.UsagePage == KeyboardControlPage && outgoing[0] == RogProtocol.CetraReportId &&
                   CetraCommands.Contains(command);
        }

        return device.UsagePage switch
        {
            ReceiverControlPage => model.Link == RogLink.Receiver && ReceiverCommands.Contains(command),
            KeyboardControlPage or MouseControlPage => DeviceCommands.Contains(command),
            _ => false,
        };
    }
}
