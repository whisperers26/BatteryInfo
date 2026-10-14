using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Tests.Monitoring;

public class LowBatteryPolicyTests
{
    private readonly LowBatteryPolicy _policy = new() { Threshold = 20 };

    private static DeviceState State(int? percent, ChargeState charge = ChargeState.Discharging,
        DeviceStatus status = DeviceStatus.Online, string id = "logitech:1")
    {
        var reading = new DeviceReading(id, "Logitech", "PRO X2 SUPERSTRIKE", DeviceKind.Mouse, status,
            [BatteryComponent.Single(percent, charge)], DateTimeOffset.UnixEpoch, "test");
        return new DeviceState(reading, reading);
    }

    private static DeviceState Earbuds(int left, int right)
    {
        var reading = new DeviceReading("huawei:1", "HUAWEI", "HUAWEI FreeClip 2", DeviceKind.Earbuds, DeviceStatus.Online,
            [new("Left", left, ChargeState.Discharging), new("Right", right, ChargeState.Discharging), new("Case", 100, ChargeState.Discharging)],
            DateTimeOffset.UnixEpoch, "SPP");
        return new DeviceState(reading, reading);
    }

    // The HUAWEI provider's fallback when SPP fails: one approximate value from Windows, charge unknown.
    private static DeviceState EarbudsOverHfp(int percent)
    {
        var reading = new DeviceReading("huawei:1", "HUAWEI", "HUAWEI FreeClip 2", DeviceKind.Earbuds, DeviceStatus.Online,
            [BatteryComponent.Single(percent, ChargeState.Unknown)], DateTimeOffset.UnixEpoch, "HFP", Approximate: true);
        return new DeviceState(reading, reading);
    }

    [Fact]
    public void Fires_once_when_reaching_threshold()
    {
        var alert = Assert.Single(_policy.Evaluate([State(20)]));
        Assert.Equal(20, alert.Percent);
        Assert.Equal("PRO X2 SUPERSTRIKE", alert.DeviceName);

        Assert.Empty(_policy.Evaluate([State(19)]));
    }

    [Fact]
    public void Does_not_fire_above_threshold()
    {
        Assert.Empty(_policy.Evaluate([State(21)]));
    }

    [Fact]
    public void Rearms_after_rising_above_threshold_plus_five()
    {
        _policy.Evaluate([State(18)]);
        Assert.Empty(_policy.Evaluate([State(25)]));
        Assert.Empty(_policy.Evaluate([State(18)]));

        Assert.Empty(_policy.Evaluate([State(26)]));
        Assert.Single(_policy.Evaluate([State(20)]));
    }

    [Fact]
    public void Rearms_when_charging_starts()
    {
        _policy.Evaluate([State(15)]);
        Assert.Empty(_policy.Evaluate([State(15, ChargeState.Charging)]));

        Assert.Single(_policy.Evaluate([State(15)]));
    }

    [Theory]
    [InlineData(ChargeState.Charging)]
    [InlineData(ChargeState.Full)]
    [InlineData(ChargeState.NotCharging)]
    public void Does_not_fire_while_on_power(ChargeState charge)
    {
        Assert.Empty(_policy.Evaluate([State(10, charge)]));
    }

    [Fact]
    public void Unknown_charge_state_still_fires()
    {
        Assert.Single(_policy.Evaluate([State(10, ChargeState.Unknown)]));
    }

    [Theory]
    [InlineData(DeviceStatus.Offline)]
    [InlineData(DeviceStatus.Asleep)]
    public void Does_not_fire_for_devices_not_read_now(DeviceStatus status)
    {
        Assert.Empty(_policy.Evaluate([State(10, status: status)]));
    }

    [Fact]
    public void Does_not_fire_for_stale_values()
    {
        var fresh = State(10);
        var stale = new DeviceState(fresh.Latest with { Components = [] }, fresh.Latest);

        Assert.Empty(_policy.Evaluate([stale]));
    }

    [Fact]
    public void Tracks_earbud_components_independently()
    {
        var first = Assert.Single(_policy.Evaluate([Earbuds(left: 15, right: 40)]));
        Assert.Equal("Left", first.Component);

        var second = Assert.Single(_policy.Evaluate([Earbuds(left: 14, right: 19)]));
        Assert.Equal("Right", second.Component);
    }

    [Fact]
    public void Hfp_fallback_does_not_alert_again_after_the_earbuds_alerted()
    {
        Assert.Equal(2, _policy.Evaluate([Earbuds(left: 15, right: 16)]).Count);

        Assert.Empty(_policy.Evaluate([EarbudsOverHfp(19)]));
    }

    [Fact]
    public void Hfp_fallback_does_not_use_up_an_earbud_alert()
    {
        var first = Assert.Single(_policy.Evaluate([Earbuds(left: 15, right: 40)]));
        Assert.Equal("Left", first.Component);
        Assert.Empty(_policy.Evaluate([EarbudsOverHfp(19)]));

        var second = Assert.Single(_policy.Evaluate([Earbuds(left: 14, right: 19)]));
        Assert.Equal("Right", second.Component);
    }

    [Fact]
    public void Threshold_change_applies_to_next_evaluation()
    {
        _policy.Threshold = 30;

        Assert.Single(_policy.Evaluate([State(29)]));
    }
}
