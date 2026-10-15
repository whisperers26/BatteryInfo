using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public class TrayLevelTests
{
    private static DeviceState State(string id, DeviceStatus status, params BatteryComponent[] components) =>
        new(new DeviceReading(id, "Vendor", id, DeviceKind.Mouse, status, components, DateTimeOffset.UnixEpoch, "test"), null);

    [Fact]
    public void The_first_device_gives_the_level()
    {
        var level = TrayLevel.First(
        [
            State("a", DeviceStatus.Online, BatteryComponent.Single(64, ChargeState.Discharging)),
            State("b", DeviceStatus.Online, BatteryComponent.Single(12, ChargeState.Discharging)),
        ]);

        Assert.Equal(new TrayLevel(64, false), level);
    }

    [Fact]
    public void Devices_without_a_live_value_are_skipped()
    {
        var level = TrayLevel.First(
        [
            State("a", DeviceStatus.NeedsAccess),
            State("b", DeviceStatus.Online),
            State("c", DeviceStatus.Online, BatteryComponent.Single(40, ChargeState.Charging)),
        ]);

        Assert.Equal(new TrayLevel(40, true), level);
    }

    [Fact]
    public void Earbuds_give_their_lowest_value_and_charge_if_either_does()
    {
        var level = TrayLevel.First(
        [
            State("a", DeviceStatus.Online, new BatteryComponent("Left", 80, ChargeState.Discharging),
                new BatteryComponent("Right", 70, ChargeState.Charging)),
        ]);

        Assert.Equal(new TrayLevel(70, true), level);
    }

    [Fact]
    public void The_case_is_left_out_while_an_earbud_has_a_value()
    {
        var level = TrayLevel.First(
        [
            State("a", DeviceStatus.Online, new BatteryComponent("Left", 80, ChargeState.Discharging),
                new BatteryComponent("Right", 70, ChargeState.Discharging), new BatteryComponent("Case", 30, ChargeState.Charging)),
        ]);

        Assert.Equal(new TrayLevel(70, false), level);
    }

    [Fact]
    public void The_case_gives_the_level_when_it_is_the_only_value()
    {
        var level = TrayLevel.First(
        [
            State("a", DeviceStatus.Online, new BatteryComponent("Left", null, ChargeState.Unknown),
                new BatteryComponent("Case", 30, ChargeState.Charging)),
        ]);

        Assert.Equal(new TrayLevel(30, true), level);
    }

    [Fact]
    public void No_device_gives_no_level()
    {
        Assert.Null(TrayLevel.First([]));
    }
}
