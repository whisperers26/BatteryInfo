using System.Globalization;
using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Vendors.Razer;

/// <summary>How the host reaches the device.</summary>
public enum RazerLink
{
    /// <summary>The device itself on its USB cable.</summary>
    Wired,

    /// <summary>The device through its HyperSpeed USB dongle.</summary>
    Dongle,

    /// <summary>Bluetooth LE (not read by the 90-byte protocol; razer.md §9).</summary>
    Bluetooth,
}

/// <summary>One supported Razer product. The cable PID names the device, so its cable and dongle links are
/// one device.</summary>
/// <param name="TransactionId">The txid to send (razer.md §5).</param>
public sealed record RazerModel(
    string Name,
    DeviceKind Kind,
    ushort WiredPid,
    ushort? DonglePid,
    ushort? BluetoothPid,
    byte TransactionId = RazerProtocol.DefaultTransactionId)
{
    public string DeviceId => "razer:" + WiredPid.ToString("X4", CultureInfo.InvariantCulture);
}

public static class RazerModels
{
    public const ushort UsbVendorId = 0x1532;

    /// <summary>Razer's Bluetooth SIG company id, as BLE HID paths show it (VID&amp;02068E).</summary>
    public const ushort BluetoothVendorId = 0x068E;

    /// <summary>Add a product here (PIDs from openrazer/OpenRGB) to read it; its commands must also be on the
    /// allow-list in <see cref="RazerCommandPolicy"/>.</summary>
    public static IReadOnlyList<RazerModel> All { get; } =
    [
        new("Razer Basilisk V3 Pro 35K", DeviceKind.Mouse, WiredPid: 0x00D6, DonglePid: 0x00D7, BluetoothPid: 0x00D8),
    ];

    /// <summary>The model and link behind a USB VID/PID, or null for anything this app does not read.</summary>
    public static (RazerModel Model, RazerLink Link)? FindUsb(ushort vendorId, ushort productId)
    {
        if (vendorId != UsbVendorId) return null;
        foreach (var model in All)
        {
            if (model.WiredPid == productId) return (model, RazerLink.Wired);
            if (model.DonglePid == productId) return (model, RazerLink.Dongle);
        }
        return null;
    }

    public static bool IsKnownUsb(ushort vendorId, ushort productId) => FindUsb(vendorId, productId) is not null;
}
