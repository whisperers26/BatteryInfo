using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Vendors.Logitech;

/// <summary>The Logitech products this app talks HID++ to (docs/protocols/logitech.md §1).</summary>
internal static class LogitechModels
{
    public const ushort VendorId = 0x046D;

    // LIGHTSPEED (C539 .. C54D) and Unifying (C52B, C532) receivers share the B5 register layout. Bolt (C548) does
    // not and is left out.
    private static readonly HashSet<ushort> Receivers = [0xC52B, 0xC532, 0xC539, 0xC53A, 0xC53D, 0xC53F, 0xC541, 0xC545, 0xC547, 0xC54D];

    // Devices on their own USB cable, addressed as device index 0xFF.
    private static readonly HashSet<ushort> WiredDevices = [0xC0A8];

    private static readonly (ushort Wpid, ushort UsbPid, string Name, DeviceKind Kind)[] Models =
    [
        (0x40BD, 0xC0A8, "PRO X2 SUPERSTRIKE", DeviceKind.Mouse),
    ];

    /// <summary>Enumeration filter: only these products are ever opened.</summary>
    public static bool IsHidppProduct(ushort vendorId, ushort productId) =>
        vendorId == VendorId && (Receivers.Contains(productId) || WiredDevices.Contains(productId));

    public static bool IsWired(ushort productId) => WiredDevices.Contains(productId);

    /// <summary>A model's name and kind by wireless product id or USB product id, when known.</summary>
    public static (string Name, DeviceKind Kind)? Find(ushort? wpid, ushort? usbPid)
    {
        foreach (var model in Models)
        {
            if (model.Wpid == wpid || model.UsbPid == usbPid) return (model.Name, model.Kind);
        }
        return null;
    }
}
