using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public class DeviceOrderTests
{
    private static DeviceState State(string id) =>
        new(new DeviceReading(id, "Vendor", id, DeviceKind.Mouse, DeviceStatus.Online, [BatteryComponent.Single(50, ChargeState.Discharging)],
            DateTimeOffset.UnixEpoch, "test"), null);

    private static IEnumerable<string> Ids(IReadOnlyList<DeviceState> states) => states.Select(s => s.Latest.DeviceId);

    [Fact]
    public void Sort_puts_saved_devices_in_the_saved_order()
    {
        var sorted = DeviceOrder.Sort([State("a"), State("b"), State("c")], ["c", "a", "b"]);

        Assert.Equal(["c", "a", "b"], Ids(sorted));
    }

    [Fact]
    public void Sort_puts_devices_not_saved_last_in_their_own_order()
    {
        var sorted = DeviceOrder.Sort([State("a"), State("b"), State("c"), State("d")], ["c", "x", "a"]);

        Assert.Equal(["c", "a", "b", "d"], Ids(sorted));
    }

    [Fact]
    public void Sort_ignores_the_case_of_ids()
    {
        var sorted = DeviceOrder.Sort([State("a"), State("B")], ["b", "A"]);

        Assert.Equal(["B", "a"], Ids(sorted));
    }

    [Fact]
    public void Merge_keeps_hidden_devices_where_they_were()
    {
        // a is disconnected; c is dragged above b.
        var order = DeviceOrder.Merge(["a", "b", "c"], ["c", "b"]);

        Assert.Equal(["a", "c", "b"], order);
    }

    [Fact]
    public void Merge_appends_devices_that_were_not_saved()
    {
        var order = DeviceOrder.Merge(["a", "b"], ["c", "b"]);

        Assert.Equal(["a", "c", "b"], order.Take(3));
        Assert.Equal(3, order.Count);
    }

    [Fact]
    public void A_device_dragged_to_the_top_while_the_first_is_away_goes_back_behind_it()
    {
        // a was first; while it is disconnected c is dragged to the top. When a returns, it is first again.
        var order = DeviceOrder.Merge(["a", "b", "c"], ["c", "b"]);

        Assert.Equal(["a", "c", "b"], Ids(DeviceOrder.Sort([State("a"), State("b"), State("c")], order)));
    }

    [Fact]
    public void Merge_with_no_saved_order_is_the_new_order()
    {
        Assert.Equal(["b", "a"], DeviceOrder.Merge([], ["b", "a"]));
    }
}
