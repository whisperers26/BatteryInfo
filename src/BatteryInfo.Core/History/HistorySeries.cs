using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.History;

public sealed record HistoryPoint(DateTimeOffset Time, int Percent, ChargeState Charge)
{
    public bool IsCharging => Charge == ChargeState.Charging;
}

/// <summary>The history of one battery component, ready to draw: runs of points with no gap longer than the
/// maximum, so a chart can break the line where the device was not read.</summary>
public sealed record HistorySeries(string Component, IReadOnlyList<IReadOnlyList<HistoryPoint>> Segments)
{
    /// <summary>One series per component of one device's records. A device with named components (Left, Right, Case)
    /// charts only those: HUAWEI earbuds also have single values from Windows' approximate HFP fallback, which would
    /// otherwise show as a fourth battery.</summary>
    public static IReadOnlyList<HistorySeries> Build(IEnumerable<HistoryRecord> records, TimeSpan maxGap)
    {
        var withPercent = records.Where(r => r.Percent is not null).ToList();
        var hasNamed = withPercent.Any(r => r.Component.Length > 0);
        return withPercent
            .Where(r => !hasNamed || r.Component.Length > 0)
            .GroupBy(r => r.Component, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => ComponentOrder.Rank(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new HistorySeries(g.Key, Split(g.OrderBy(r => r.Time), maxGap)))
            .ToList();
    }

    private static IReadOnlyList<IReadOnlyList<HistoryPoint>> Split(IEnumerable<HistoryRecord> ordered, TimeSpan maxGap)
    {
        var segments = new List<IReadOnlyList<HistoryPoint>>();
        List<HistoryPoint>? current = null;
        foreach (var record in ordered)
        {
            var point = new HistoryPoint(record.Time, record.Percent!.Value, record.Charge);
            if (current is null || point.Time - current[^1].Time > maxGap)
            {
                current = [];
                segments.Add(current);
            }
            current.Add(point);
        }
        return segments;
    }
}
