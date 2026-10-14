namespace BatteryInfo.Core.Model;

/// <summary>One battery of a device. <see cref="Name"/> is "" for a single-battery device,
/// otherwise "Left", "Right" or "Case".</summary>
public sealed record BatteryComponent(string Name, int? Percent, ChargeState Charge)
{
    public static BatteryComponent Single(int? percent, ChargeState charge) => new("", percent, charge);
}
