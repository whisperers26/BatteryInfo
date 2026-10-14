using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public class ConnectedDevicesTests
{
    private static DeviceState State(string id, DeviceStatus status) =>
        new(new DeviceReading(id, "Vendor", id, DeviceKind.Mouse, status, [BatteryComponent.Single(50, ChargeState.Discharging)],
            DateTimeOffset.UnixEpoch, "test"), null);

    [Theory]
    [InlineData(DeviceStatus.Online, true)]
    [InlineData(DeviceStatus.NeedsAccess, true)]
    [InlineData(DeviceStatus.Error, true)]
    [InlineData(DeviceStatus.Asleep, false)]
    [InlineData(DeviceStatus.Offline, false)]
    public void Only_devices_talking_to_the_pc_are_shown(DeviceStatus status, bool shown)
    {
        Assert.Equal(shown, ConnectedDevices.IsShown(State("a", status)));
    }

    [Fact]
    public void Filter_keeps_the_order_of_the_shown_devices()
    {
        var shown = ConnectedDevices.Filter(
        [
            State("a", DeviceStatus.Online),
            State("b", DeviceStatus.Offline),
            State("c", DeviceStatus.Asleep),
            State("d", DeviceStatus.NeedsAccess),
        ]);

        Assert.Equal(["a", "d"], shown.Select(s => s.Latest.DeviceId));
    }
}
