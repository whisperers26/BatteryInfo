using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Tests.Model;

public class DeviceReadingTests
{
    private static DeviceReading Reading(params BatteryComponent[] components) =>
        new("huawei:C0DA5EA83EC7", "HUAWEI", "HUAWEI FreeClip 2", DeviceKind.Earbuds, DeviceStatus.Online,
            components, DateTimeOffset.UnixEpoch, "SPP");

    [Fact]
    public void LowestPercent_ignores_unknown_components()
    {
        var reading = Reading(
            new BatteryComponent("Left", 80, ChargeState.Discharging),
            new BatteryComponent("Right", null, ChargeState.Unknown),
            new BatteryComponent("Case", 100, ChargeState.Full));

        Assert.True(reading.HasBattery);
        Assert.Equal(80, reading.LowestPercent);
    }

    [Fact]
    public void Reading_without_components_has_no_battery()
    {
        var reading = Reading();

        Assert.False(reading.HasBattery);
        Assert.Null(reading.LowestPercent);
    }

    [Fact]
    public void Single_creates_unnamed_component()
    {
        var component = BatteryComponent.Single(56, ChargeState.Discharging);

        Assert.Equal("", component.Name);
        Assert.Equal(56, component.Percent);
    }
}
