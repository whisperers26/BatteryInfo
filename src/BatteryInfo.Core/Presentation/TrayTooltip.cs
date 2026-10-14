using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Presentation;

/// <summary>The tray icon's tooltip: one compact line per device, fitted into the 127 characters Windows allows.</summary>
public static class TrayTooltip
{
    public const int MaxLength = 127;
    public const string Title = "BatteryInfo";
    private const int MinNameLength = 6;

    public static string Build(IReadOnlyList<DeviceState> states)
    {
        if (states.Count == 0) return Title + "\nNo devices found";

        var lines = states.Select(s => (Name: s.Latest.Name, Value: Value(s))).ToList();
        var longestName = lines.Max(l => l.Name.Length);

        // Prefer the full text, then lose the header, then shorten names, then drop the last devices.
        var full = Join(lines, longestName, header: true);
        if (full.Length <= MaxLength) return full;

        for (var cap = longestName; cap >= MinNameLength; cap--)
        {
            var text = Join(lines, cap, header: false);
            if (text.Length <= MaxLength) return text;
        }

        for (var count = lines.Count - 1; count > 0; count--)
        {
            var text = Join(lines.Take(count).ToList(), MinNameLength, header: false) + $"\n+{lines.Count - count} more";
            if (text.Length <= MaxLength) return text;
        }
        return Join(lines, MinNameLength, header: false)[..MaxLength];
    }

    private static string Join(IReadOnlyList<(string Name, string Value)> lines, int nameCap, bool header)
    {
        var body = string.Join('\n', lines.Select(l => $"{Shorten(l.Name, nameCap)}: {l.Value}"));
        return header ? Title + "\n" + body : body;
    }

    private static string Shorten(string name, int cap) =>
        name.Length <= cap ? name : name[..(cap - 1)].TrimEnd() + "…";

    private static string Value(DeviceState state)
    {
        var reading = state.Latest;
        switch (reading.Status)
        {
            case DeviceStatus.Asleep:
                return "asleep";
            case DeviceStatus.Offline:
                return "not connected";
            case DeviceStatus.NeedsAccess:
                return "needs access";
            case DeviceStatus.Error:
                return "error";
        }

        var components = reading.Components.Where(c => c.Percent is not null).ToList();
        if (components.Count == 0) return "no value";
        var approx = reading.Approximate ? "≈" : "";
        if (components.Count == 1)
        {
            var single = components[0];
            return $"{approx}{single.Percent}%{(single.Charge == ChargeState.Charging ? " charging" : "")}";
        }
        return string.Join(' ', components.Select(c => $"{Label(c.Name)}{approx}{c.Percent}%"));
    }

    private static string Label(string component) => component switch
    {
        "" => "",
        _ => component[..1].ToUpperInvariant(),
    };
}
