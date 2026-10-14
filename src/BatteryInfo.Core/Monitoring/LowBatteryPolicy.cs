using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Monitoring;

public sealed record LowBatteryAlert(string DeviceId, string DeviceName, string Component, int Percent);

/// <summary>Decides when to warn about a low battery: once per discharge, per device component.</summary>
public sealed class LowBatteryPolicy
{
    private const int RearmMargin = 5;
    private readonly HashSet<string> _alerted = new(StringComparer.OrdinalIgnoreCase);

    public int Threshold { get; set; } = 20;

    public IReadOnlyList<LowBatteryAlert> Evaluate(IReadOnlyList<DeviceState> states)
    {
        var alerts = new List<LowBatteryAlert>();
        foreach (var state in states)
        {
            var reading = state.Latest;
            if (reading.Status != DeviceStatus.Online) continue;

            foreach (var component in reading.Components)
            {
                if (component.Percent is not { } percent) continue;
                var key = $"{reading.DeviceId}|{component.Name}";

                if (IsOnPower(component.Charge) || percent > Threshold + RearmMargin)
                {
                    _alerted.Remove(key);
                    continue;
                }

                if (percent <= Threshold && _alerted.Add(key))
                {
                    alerts.Add(new LowBatteryAlert(reading.DeviceId, reading.Name, component.Name, percent));
                }
            }
        }
        return alerts;
    }

    private static bool IsOnPower(ChargeState charge) =>
        charge is ChargeState.Charging or ChargeState.Full or ChargeState.NotCharging;
}
