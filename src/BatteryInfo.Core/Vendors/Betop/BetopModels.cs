namespace BatteryInfo.Core.Vendors.Betop;

/// <summary>A Betop gen-4 "KP" pad that speaks the plain-byte (SetHidWriteEnc 0) protocol.</summary>
/// <param name="Name">Model name as BetopGame uses it, e.g. "KP70C2".</param>
/// <param name="WiredProductId">PID of the pad on a USB cable (VID 0x20BC).</param>
/// <param name="DongleProductId">PID of its dongle with the pad linked (VID 0x20BC).</param>
/// <param name="ChargeInLowNibble">True when only the low nibble of reply byte 4 is the charge state; the KP20
/// and KP40 families use the whole byte.</param>
public sealed record BetopModel(string Name, ushort WiredProductId, ushort DongleProductId, bool ChargeInLowNibble = true)
{
    /// <summary>Shared by the wired and dongle paths of one pad.</summary>
    public string DeviceId => $"betop:{Name}";

    public string DisplayName => $"Betop {Name}";
}

/// <summary>The pads this app may talk to. Only KP70C2 (518C/518D) was tested on hardware; the siblings come from
/// BetopGame's model scripts (same report id 2, same 0x15 request and reply).</summary>
public static class BetopModels
{
    /// <summary>The only vendor id that may ever be written to.</summary>
    public const ushort OnlineVendorId = 0x20BC;

    /// <summary>The KP70C2 dongle re-enumerates with this vendor id while the pad sleeps. Never written to.</summary>
    public const ushort AsleepVendorId = 0x20DD;

    // Models with SetHidWriteEnc 1 (KP20A, KP40A, KP40B, KP40D-b1, KP40F) substitute every outgoing byte through a
    // table, so a plain request would reach them as a different command. They are deliberately absent.
    public static IReadOnlyList<BetopModel> All { get; } =
    [
        new("KP20D", 0x5158, 0x5159, ChargeInLowNibble: false),
        new("KP20DL", 0x516F, 0x5170, ChargeInLowNibble: false),
        new("KP20EB", 0x5190, 0x5191, ChargeInLowNibble: false),
        new("KP40D", 0x515B, 0x515C, ChargeInLowNibble: false),
        new("KP40MCF", 0x5165, 0x5166, ChargeInLowNibble: false),
        new("KP40MCK", 0x5163, 0x5164, ChargeInLowNibble: false),
        new("KP50C", 0x5149, 0x514A),
        new("KP50D", 0x5150, 0x5151),
        new("KP50E", 0x5152, 0x5153),
        new("KP50B", 0x5133, 0x5134),
        new("KP70A", 0x512F, 0x5130),
        new("KP70A1", 0x516D, 0x516E),
        new("KP70B", 0x515F, 0x5160),
        new("KP70C", 0x5173, 0x5174),
        new("KP70C1", 0x518A, 0x518B),
        new("KP70C2", 0x518C, 0x518D),
        new("PG01A", 0x5161, 0x5162),
    ];

    /// <summary>The model with this wired or dongle PID, or null.</summary>
    public static BetopModel? FindByProductId(ushort productId) =>
        All.FirstOrDefault(m => m.WiredProductId == productId || m.DongleProductId == productId);

    /// <summary>The model whose pad is asleep when this VID/PID is present: only the KP70C2 dongle as 20DD:518D.
    /// Other models register 20DD under their wired PID as a normal device, so this is not generalised.</summary>
    public static BetopModel? FindAsleepDongle(ushort vendorId, ushort productId) =>
        vendorId == AsleepVendorId && productId == Kp70C2DongleProductId ? FindByProductId(productId) : null;

    private const ushort Kp70C2DongleProductId = 0x518D;
}
