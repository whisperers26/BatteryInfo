using BatteryInfo.Core.History;
using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Tests.History;

public class HistorySeriesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static HistoryRecord Record(double minutes, string component, int? percent, ChargeState charge = ChargeState.Discharging) =>
        new(T0.AddMinutes(minutes), "huawei:C0DA5EA83EC7", "HUAWEI FreeClip 2", "HUAWEI", DeviceKind.Earbuds, component,
            percent, charge, DeviceStatus.Online);

    [Fact]
    public void Build_makes_one_series_per_component_in_display_order()
    {
        var series = HistorySeries.Build(
        [
            Record(0, "Case", 100), Record(0, "Right", 75), Record(0, "Left", 76),
            Record(10, "Left", 74),
        ], TimeSpan.FromMinutes(45));

        Assert.Equal(["Left", "Right", "Case"], series.Select(s => s.Component));
        var left = Assert.Single(series[0].Segments);
        Assert.Equal([76, 74], left.Select(p => p.Percent));
        Assert.Equal([T0, T0.AddMinutes(10)], left.Select(p => p.Time));
    }

    [Fact]
    public void Build_sorts_points_by_time_and_splits_at_gaps_longer_than_maxGap()
    {
        var series = HistorySeries.Build(
        [
            Record(110, "", 50), Record(0, "", 60), Record(30, "", 58), Record(60, "", 56),
            Record(200, "", 48),
        ], TimeSpan.FromMinutes(45));

        var single = Assert.Single(series);
        Assert.Equal(3, single.Segments.Count);
        Assert.Equal([60, 58, 56], single.Segments[0].Select(p => p.Percent));
        Assert.Equal([50], single.Segments[1].Select(p => p.Percent));
        Assert.Equal([48], single.Segments[2].Select(p => p.Percent));
    }

    [Fact]
    public void Build_skips_records_without_a_percent_and_keeps_charging_flags()
    {
        var series = HistorySeries.Build(
        [
            Record(0, "", 40, ChargeState.Charging), Record(10, "", null), Record(20, "", 45, ChargeState.Full),
        ], TimeSpan.FromMinutes(45));

        var points = Assert.Single(Assert.Single(series).Segments);
        Assert.Equal([40, 45], points.Select(p => p.Percent));
        Assert.True(points[0].IsCharging);
        Assert.False(points[1].IsCharging);
    }

    [Fact]
    public void Build_of_nothing_is_empty()
    {
        Assert.Empty(HistorySeries.Build([], TimeSpan.FromMinutes(45)));
    }
}
