using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Presentation;

/// <summary>The level the tray icon shows when the user turns that on: the first listed device with a live value.</summary>
public sealed record TrayLevel(int Percent, bool Charging)
{
    private const string Case = "Case";

    /// <summary>At or below the low-battery alert level and not charging: when an alert would fire.</summary>
    public bool IsLow(int threshold) => !Charging && Percent <= threshold;

    /// <summary>From the devices as listed (connected, in the user's order); null when none has a live value. A device
    /// with several batteries gives the lowest of the ones in use, and counts as charging when any of them is: the case
    /// is left out while an earbud has a value, and gives the level only when nothing else does. Its low-battery alert
    /// is unaffected.</summary>
    public static TrayLevel? First(IReadOnlyList<DeviceState> shown)
    {
        var reading = shown.Select(s => s.Latest).FirstOrDefault(r => r.Status == DeviceStatus.Online && r.HasBattery);
        if (reading is null) return null;

        var read = reading.Components.Where(c => c.Percent is not null).ToList();
        var inUse = read.Where(c => !c.Name.Equals(Case, StringComparison.OrdinalIgnoreCase)).ToList();
        if (inUse.Count == 0) inUse = read;
        return new TrayLevel(inUse.Min(c => c.Percent!.Value), inUse.Any(c => c.Charge == ChargeState.Charging));
    }
}
