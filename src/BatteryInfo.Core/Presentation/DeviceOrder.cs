using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Presentation;

/// <summary>The order the user dragged the devices into, saved as device ids. It covers disconnected devices too, so
/// one that comes back takes its old place.</summary>
public static class DeviceOrder
{
    /// <summary>The states in the saved order; devices not in it follow, in the order they came.</summary>
    public static IReadOnlyList<DeviceState> Sort(IReadOnlyList<DeviceState> states, IReadOnlyList<string> order)
    {
        var rank = Rank(order);
        return states
            .Select((state, index) => (state, index))
            .OrderBy(x => rank.TryGetValue(x.state.Latest.DeviceId, out var r) ? r : int.MaxValue)
            .ThenBy(x => x.index)
            .Select(x => x.state)
            .ToList();
    }

    /// <summary>The saved order after the shown devices were rearranged into <paramref name="shown"/>. The shown devices
    /// trade places among the slots they held, so hidden ones keep theirs; shown ones not saved yet are appended.</summary>
    public static IReadOnlyList<string> Merge(IReadOnlyList<string> saved, IReadOnlyList<string> shown)
    {
        var shownSet = new HashSet<string>(shown, StringComparer.OrdinalIgnoreCase);
        var savedSet = new HashSet<string>(saved, StringComparer.OrdinalIgnoreCase);
        var full = saved.Concat(shown.Where(id => !savedSet.Contains(id))).ToList();
        var next = 0;
        for (var i = 0; i < full.Count; i++)
        {
            if (shownSet.Contains(full[i])) full[i] = shown[next++];
        }
        return full;
    }

    private static Dictionary<string, int> Rank(IReadOnlyList<string> order)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);
        return rank;
    }
}
