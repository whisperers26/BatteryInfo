namespace BatteryInfo.Core.Model;

/// <summary>What one provider poll learned about one device.</summary>
/// <param name="DeviceId">Stable, vendor-prefixed id, e.g. "logitech:B4655832".</param>
/// <param name="Components">Empty when no battery value was read this time.</param>
/// <param name="Source">How the value was read, e.g. "HID++ 0x1004", "SPP", "HFP (approx.)".</param>
/// <param name="Detail">Short human-readable note: errors, fallbacks.</param>
public sealed record DeviceReading(
    string DeviceId,
    string Vendor,
    string Name,
    DeviceKind Kind,
    DeviceStatus Status,
    IReadOnlyList<BatteryComponent> Components,
    DateTimeOffset Timestamp,
    string Source,
    bool Approximate = false,
    string? Detail = null)
{
    /// <summary>The <see cref="Source"/> of a value that is Windows' own HFP battery level rather than one read from the
    /// device: coarse, and often a few points high.</summary>
    public const string WindowsHfpSource = "HFP (approx.)";

    /// <summary>The <see cref="Source"/> of a value from Apple's Proximity Pairing broadcast: 10 % steps, rounded down.</summary>
    public const string AppleBroadcastSource = "BLE broadcast";

    public bool HasBattery => Components.Any(c => c.Percent is not null);

    public int? LowestPercent => Components.Where(c => c.Percent is not null).Min(c => c.Percent);
}
