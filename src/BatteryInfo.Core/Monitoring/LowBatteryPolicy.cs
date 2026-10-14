using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Monitoring;

public sealed record LowBatteryAlert(string DeviceId, string DeviceName, string Component, int Percent);

/// <summary>Decides when to warn about a low battery: once per discharge, per device component.</summary>
public sealed class LowBatteryPolicy
{
    private const int RearmMargin = 5;
    private const string WholeDevice = "";

    // Device id -> names of its components that have alerted in the current discharge.
    private readonly Dictionary<string, HashSet<string>> _alerted = new(StringComparer.OrdinalIgnoreCase);

    public int Threshold { get; set; } = 20;

    public IReadOnlyList<LowBatteryAlert> Evaluate(IReadOnlyList<DeviceState> states)
    {
        var alerts = new List<LowBatteryAlert>();
        foreach (var state in states)
        {
            var reading = state.Latest;
            if (reading.Status != DeviceStatus.Online) continue;

            var alerted = AlertedComponents(reading.DeviceId);
            foreach (var component in reading.Components)
            {
                if (component.Percent is not { } percent) continue;

                if (IsOnPower(component.Charge) || percent > Threshold + RearmMargin)
                {
                    alerted.Remove(component.Name);
                    continue;
                }

                if (percent > Threshold || alerted.Contains(component.Name)) continue;

                // A device read one battery at a time (HUAWEI over SPP) can fall back to one whole-device value (HFP):
                // once one of its batteries has alerted, that value is the same discharge, so it neither alerts nor
                // takes the place of a battery that has not alerted yet.
                if (IsWholeDevice(component) && alerted.Any(name => name.Length > 0)) continue;

                alerted.Add(component.Name);

                // The other way round: when the batteries are read again after the whole-device value alerted, the ones
                // at or below the threshold take over that alert without raising another.
                if (!IsWholeDevice(component) && alerted.Contains(WholeDevice)) continue;

                alerts.Add(new LowBatteryAlert(reading.DeviceId, reading.Name, component.Name, percent));
            }

            // Handed over: from here each battery alerts on its own. The whole-device value cannot say which battery it
            // stood for (HUAWEI's may be the right bud alone), and HFP's charge state is always unknown, so a kept
            // whole-device alert would only re-arm on another HFP reading and could silence the next discharge.
            if (reading.Components.Any(c => !IsWholeDevice(c) && c.Percent is not null)) alerted.Remove(WholeDevice);
        }
        return alerts;
    }

    private HashSet<string> AlertedComponents(string deviceId)
    {
        if (!_alerted.TryGetValue(deviceId, out var components))
        {
            components = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _alerted.Add(deviceId, components);
        }
        return components;
    }

    private static bool IsWholeDevice(BatteryComponent component) => component.Name.Length == 0;

    private static bool IsOnPower(ChargeState charge) =>
        charge is ChargeState.Charging or ChargeState.Full or ChargeState.NotCharging;
}
