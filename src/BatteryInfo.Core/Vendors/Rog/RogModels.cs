using System.Globalization;
using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Vendors.Rog;

/// <summary>How a ROG product id is reached.</summary>
public enum RogLink
{
    /// <summary>An OMNI-class receiver: list its paired devices with A0 00 and reach each through a slot collection.</summary>
    Receiver,

    /// <summary>The device on a USB cable: no alive check.</summary>
    Usb,

    /// <summary>The device on its own wireless link (own dongle or RF mode): alive check first.</summary>
    Wireless,

    /// <summary>The device over Bluetooth LE: not polled in v1.</summary>
    Bluetooth,
}

/// <param name="Slug">Model part of the device id, rog:&lt;slug&gt;:&lt;serial&gt;; shared by every PID of one product.</param>
public sealed record RogModel(ushort ProductId, string Slug, string Name, DeviceKind Kind, RogLink Link);

/// <summary>The ROG product ids this app knows (docs/protocols/rog.md §1, §6f, §8). Any other ASUS device, such as a
/// monitor's Aura controller, is never opened.</summary>
public static class RogModels
{
    public const ushort VendorId = 0x0B05;

    private static readonly Dictionary<ushort, RogModel> ByProductId = new RogModel[]
    {
        // OMNI-class receivers; 1ACF and 1AD1 are alternate modes of unknown purpose (same GET-only commands).
        new(0x1ACE, "omni-receiver", "ROG OMNI Receiver", DeviceKind.Other, RogLink.Receiver),
        new(0x1ACF, "omni-receiver", "ROG OMNI Receiver", DeviceKind.Other, RogLink.Receiver),
        new(0x1AD0, "speednova-8k-receiver", "ROG SpeedNova 8K Receiver", DeviceKind.Other, RogLink.Receiver),
        new(0x1AD1, "speednova-8k-receiver", "ROG SpeedNova 8K Receiver", DeviceKind.Other, RogLink.Receiver),
        new(0x1D54, "asus-dongle", "ASUS Dongle", DeviceKind.Other, RogLink.Receiver),

        // Azoth X: USB cable, RF (the PID an OMNI slot reports), alternate mode of unknown role, Bluetooth.
        new(0x1C24, "azoth-x", "ROG Azoth X", DeviceKind.Keyboard, RogLink.Usb),
        new(0x1C25, "azoth-x", "ROG Azoth X", DeviceKind.Keyboard, RogLink.Wireless),
        new(0x1C26, "azoth-x", "ROG Azoth X", DeviceKind.Keyboard, RogLink.Wireless),
        new(0x1C27, "azoth-x", "ROG Azoth X", DeviceKind.Keyboard, RogLink.Bluetooth),

        // Cetra Open Wireless on its own dongle (report id 0xCC).
        new(0x1CAC, "cetra-open-wireless", "ROG Cetra Open Wireless", DeviceKind.Earbuds, RogLink.Wireless),

        // Older own-dongle mice (report id 0, 65-byte packets per G-Helper/OpenRGB).
        new(0x1960, "keris-wireless", "ROG Keris Wireless", DeviceKind.Mouse, RogLink.Wireless),
        new(0x197F, "gladius-iii-wireless", "ROG Gladius III Wireless", DeviceKind.Mouse, RogLink.Wireless),
    }.ToDictionary(m => m.ProductId);

    public static IReadOnlyCollection<RogModel> All => ByProductId.Values;

    public static RogModel? Find(ushort productId) => ByProductId.GetValueOrDefault(productId);

    /// <summary>The enumeration filter: only these collections are ever opened, even for caps.</summary>
    public static bool IsSupported(ushort vendorId, ushort productId) => vendorId == VendorId && ByProductId.ContainsKey(productId);

    /// <summary>The model of a device behind a receiver: from the table, else its kind from the slot's control report id
    /// (2 keyboard, 3 mouse).</summary>
    public static RogModel ForSlot(RogSlot slot)
    {
        if (Find(slot.ProductId) is { } known) return known;
        var (kind, name) = slot.ControlReportId switch
        {
            2 => (DeviceKind.Keyboard, "ROG keyboard"),
            3 => (DeviceKind.Mouse, "ROG mouse"),
            _ => (DeviceKind.Other, "ROG device"),
        };
        return new RogModel(slot.ProductId, slot.ProductId.ToString("x4", CultureInfo.InvariantCulture), name, kind, RogLink.Wireless);
    }
}
