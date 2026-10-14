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
    public static IReadOnlyList<HistorySeries> Build(IEnumerable<HistoryRecord> records, TimeSpan maxGap) =>
        records
            .Where(r => r.Percent is not null)
            .GroupBy(r => r.Component, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => ComponentOrder.Rank(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new HistorySeries(g.Key, Split(g.OrderBy(r => r.Time), maxGap)))
            .ToList();

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
