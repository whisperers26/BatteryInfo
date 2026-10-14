using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Presentation;

/// <summary>The level the tray icon shows when the user turns that on: the first listed device with a live value.</summary>
public sealed record TrayLevel(int Percent, bool Charging)
{
    /// <summary>From the devices as listed (connected, in the user's order); null when none has a live value. A device
    /// with several batteries gives its lowest, and counts as charging when any of them is.</summary>
    public static TrayLevel? First(IReadOnlyList<DeviceState> shown)
    {
        var reading = shown.Select(s => s.Latest).FirstOrDefault(r => r.Status == DeviceStatus.Online && r.HasBattery);
        return reading is null
            ? null
            : new TrayLevel(reading.LowestPercent!.Value, reading.Components.Any(c => c.Charge == ChargeState.Charging));
    }
}
