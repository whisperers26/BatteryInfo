using BatteryInfo.App.ViewModels;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Settings;
using BatteryInfo.Core.Tests.Fakes;

namespace BatteryInfo.Core.Tests.App;

public class FlyoutDeviceOrderTests
{
    private readonly FakeFlyoutServices _services = new();
    private readonly FlyoutViewModel _flyout;

    public FlyoutDeviceOrderTests()
    {
        _flyout = new FlyoutViewModel(_services, new AppSettings());
        _flyout.Update([State("a"), State("b"), State("c")], null);
    }

    private static DeviceState State(string id) =>
        new(new DeviceReading(id, "Vendor", id, DeviceKind.Mouse, DeviceStatus.Online, [BatteryComponent.Single(50, ChargeState.Discharging)],
            DateTimeOffset.UnixEpoch, "test"), null);

    private IEnumerable<string> Rows => _flyout.Devices.Select(d => d.Id);

    [Fact]
    public void Moving_a_row_reorders_the_rows_and_saves_the_order()
    {
        _flyout.MoveDevice(_flyout.Devices[2], 0);

        Assert.Equal(["c", "a", "b"], Rows);
        Assert.Equal(["c", "a", "b"], _services.DeviceOrder!);
    }

    [Fact]
    public void Moving_a_row_down_puts_it_at_that_index()
    {
        _flyout.MoveDevice(_flyout.Devices[0], 1);

        Assert.Equal(["b", "a", "c"], Rows);
    }

    [Fact]
    public void Moving_a_row_onto_itself_saves_nothing()
    {
        _flyout.MoveDevice(_flyout.Devices[1], 1);

        Assert.Null(_services.DeviceOrder);
    }

    [Fact]
    public void An_index_past_the_end_moves_the_row_last()
    {
        _flyout.MoveDevice(_flyout.Devices[0], 10);

        Assert.Equal(["b", "c", "a"], Rows);
    }

    [Fact]
    public void The_moved_row_keeps_its_view_model()
    {
        var row = _flyout.Devices[2];

        _flyout.MoveDevice(row, 0);

        Assert.Same(row, _flyout.Devices[0]);
    }
}
