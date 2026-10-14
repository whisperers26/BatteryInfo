using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Presentation;

/// <summary>The user-facing words for a device: its status line, percentages and low-battery message.</summary>
public static class DeviceText
{
    public const string Separator = " · ";

    /// <summary>The second line of a device row: "Charging", "Asleep · 82 % 14 min ago",
    /// "Not connected · last 82 % yesterday", "Approx. from Windows", "Estimated", or the provider's detail.</summary>
    /// <param name="now">The current local time.</param>
    public static string StatusLine(DeviceState state, DateTimeOffset now)
    {
        var latest = state.Latest;
        return latest.Status switch
        {
            DeviceStatus.Online => OnlineLine(latest),
            DeviceStatus.Asleep => WithLastValue("Asleep", "", state, now),
            DeviceStatus.Offline => WithLastValue("Not connected", "last ", state, now),
            DeviceStatus.NeedsAccess => latest.Detail ?? "Windows denied access",
            _ => latest.Detail ?? "Could not read the battery",
        };
    }

    /// <summary>"56 %", "≈80 %" for an approximate value, "–" when unknown.</summary>
    public static string Percent(int? percent, bool approximate) =>
        percent is not { } value ? "–" : approximate ? $"≈{value} %" : $"{value} %";

    /// <summary>The short label shown next to a component's value: "" for a single battery, "L", "R", "Case".</summary>
    public static string ComponentLabel(string component) => component switch
    {
        "Left" => "L",
        "Right" => "R",
        _ => component,
    };

    /// <summary>"PRO X2 SUPERSTRIKE is at 18 %", "HUAWEI FreeClip 2 left earbud is at 15 %".</summary>
    public static string LowBatteryMessage(LowBatteryAlert alert)
    {
        var part = alert.Component switch
        {
            "" => "",
            "Left" => " left earbud",
            "Right" => " right earbud",
            _ => " " + alert.Component.ToLowerInvariant(),
        };
        return $"{alert.DeviceName}{part} is at {alert.Percent} %";
    }

    /// <summary>"Charging", "Fully charged", ... or "" when the state says nothing worth showing.</summary>
    public static string ChargeText(ChargeState charge) => charge switch
    {
        ChargeState.Charging => "Charging",
        ChargeState.Full => "Fully charged",
        ChargeState.NotCharging => "Plugged in, not charging",
        ChargeState.Fault => "Charging problem",
        _ => "",
    };

    private static string OnlineLine(DeviceReading reading)
    {
        var charge = ChargeLine(reading.Components);
        if (reading.Approximate)
        {
            // Only the HFP fallback is Windows' value; a device's own voltage or coarse level is an estimate.
            var approximate = IsFromWindows(reading) ? "Approx. from Windows" : "Estimated";
            return charge.Length == 0 ? approximate : approximate + Separator + charge;
        }
        if (reading.Detail is { Length: > 0 } detail) return detail;
        return charge.Length == 0 ? "Connected" : charge;
    }

    private static bool IsFromWindows(DeviceReading reading) => reading.Source == DeviceReading.WindowsHfpSource;

    private static string ChargeLine(IReadOnlyList<BatteryComponent> components)
    {
        if (components.Count == 1) return ChargeText(components[0].Charge);
        var charging = components.Where(c => c.Charge == ChargeState.Charging).Select(c => c.Name).ToList();
        if (charging.Count == 0) return "";
        return charging.Count == components.Count ? "Charging" : string.Join(", ", charging) + " charging";
    }

    private static string WithLastValue(string status, string lastPrefix, DeviceState state, DateTimeOffset now)
    {
        var components = state.DisplayComponents;
        if (state.BatteryTimestamp is not { } readAt || !components.Any(c => c.Percent is not null)) return status;

        var age = RelativeTime.Format(readAt, now);
        if (components.Count == 1)
        {
            var approximate = (state.Latest.HasBattery ? state.Latest : state.LastWithBattery)?.Approximate ?? false;
            return $"{status}{Separator}{lastPrefix}{Percent(components[0].Percent, approximate)} {age}";
        }
        return $"{status}{Separator}last read {age}";
    }
}
