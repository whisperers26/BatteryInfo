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

    private static DeviceState Earbuds(int left, int right, ChargeState charge = ChargeState.Discharging, int @case = 100)
    {
        var reading = new DeviceReading("huawei:1", "HUAWEI", "HUAWEI FreeClip 2", DeviceKind.Earbuds, DeviceStatus.Online,
            [new("Left", left, charge), new("Right", right, charge), new("Case", @case, charge)],
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
    public void Case_alert_does_not_silence_the_hfp_fallback()
    {
        var caseAlert = Assert.Single(_policy.Evaluate([Earbuds(left: 80, right: 80, @case: 18)]));
        Assert.Equal("Case", caseAlert.Component);

        var alert = Assert.Single(_policy.Evaluate([EarbudsOverHfp(20)]));
        Assert.Equal("", alert.Component);
        Assert.Empty(_policy.Evaluate([EarbudsOverHfp(19)]));
    }

    [Fact]
    public void Earbuds_do_not_alert_again_after_the_hfp_fallback_alerted()
    {
        Assert.Single(_policy.Evaluate([EarbudsOverHfp(20)]));

        Assert.Empty(_policy.Evaluate([Earbuds(left: 14, right: 40)]));
        Assert.Empty(_policy.Evaluate([EarbudsOverHfp(19)]));
        Assert.Empty(_policy.Evaluate([Earbuds(left: 13, right: 39)]));
    }

    [Fact]
    public void Hfp_alert_does_not_use_up_the_other_earbud_alert()
    {
        Assert.Single(_policy.Evaluate([EarbudsOverHfp(20)]));
        Assert.Empty(_policy.Evaluate([Earbuds(left: 14, right: 40)]));

        var alert = Assert.Single(_policy.Evaluate([Earbuds(left: 13, right: 19)]));
        Assert.Equal("Right", alert.Component);
    }

    [Fact]
    public void Hfp_alert_rearms_when_the_earbuds_charge()
    {
        Assert.Single(_policy.Evaluate([EarbudsOverHfp(20)]));
        Assert.Empty(_policy.Evaluate([Earbuds(left: 60, right: 60, ChargeState.Charging)]));

        var alert = Assert.Single(_policy.Evaluate([Earbuds(left: 20, right: 40)]));
        Assert.Equal("Left", alert.Component);
    }

    [Fact]
    public void Hfp_alert_rearms_when_the_earbuds_come_back_charged_whatever_the_case_reads()
    {
        Assert.Single(_policy.Evaluate([EarbudsOverHfp(20)]));
        Assert.Empty(_policy.Evaluate([Earbuds(left: 90, right: 90, @case: 22)]));

        var alert = Assert.Single(_policy.Evaluate([Earbuds(left: 20, right: 85, @case: 22)]));
        Assert.Equal("Left", alert.Component);
    }

    [Fact]
    public void Case_does_not_take_over_the_hfp_alert()
    {
        Assert.Single(_policy.Evaluate([EarbudsOverHfp(20)]));

        var alert = Assert.Single(_policy.Evaluate([Earbuds(left: 60, right: 60, @case: 19)]));
        Assert.Equal("Case", alert.Component);
        Assert.Equal(19, alert.Percent);
        Assert.Empty(_policy.Evaluate([Earbuds(left: 60, right: 60, @case: 17)]));
    }

    [Fact]
    public void Threshold_change_applies_to_next_evaluation()
    {
        _policy.Threshold = 30;

        Assert.Single(_policy.Evaluate([State(29)]));
    }

    // A restart: a new policy at the user's threshold, given what this one had alerted.
    private LowBatteryPolicy Restart(int threshold = 20)
    {
        var restarted = new LowBatteryPolicy { Threshold = threshold };
        restarted.Restore(_policy.Alerted);
        return restarted;
    }

    [Fact]
    public void Does_not_fire_again_after_a_restart_for_a_component_already_alerted()
    {
        Assert.Single(_policy.Evaluate([State(15)]));

        var restarted = Restart();

        Assert.Empty(restarted.Evaluate([State(15)]));
        Assert.Empty(restarted.Evaluate([State(14)]));
    }

    [Fact]
    public void Fires_after_a_restart_for_a_value_not_alerted_under_a_lower_threshold()
    {
        _policy.Threshold = 10;
        Assert.Empty(_policy.Evaluate([State(15)]));
        _policy.Threshold = 20; // raised while the device is off, so nothing is evaluated

        Assert.Single(Restart(threshold: 20).Evaluate([State(15)]));
    }

    [Fact]
    public void Fires_after_a_restart_when_the_component_was_rearmed_before_it()
    {
        _policy.Evaluate([State(15)]);
        _policy.Evaluate([State(15, ChargeState.Charging)]);

        Assert.Single(Restart().Evaluate([State(15)]));
    }

    [Fact]
    public void Restored_component_rearms_when_charging_starts()
    {
        _policy.Evaluate([State(15)]);
        var restarted = Restart();

        Assert.Empty(restarted.Evaluate([State(15, ChargeState.Charging)]));
        Assert.Single(restarted.Evaluate([State(15)]));
    }

    [Fact]
    public void Restores_earbud_components_independently()
    {
        _policy.Evaluate([Earbuds(left: 15, right: 40)]);

        var alert = Assert.Single(Restart().Evaluate([Earbuds(left: 14, right: 19)]));
        Assert.Equal("Right", alert.Component);
    }

    [Fact]
    public void Alerted_is_a_copy_that_later_evaluations_do_not_change()
    {
        _policy.Evaluate([State(15)]);
        var alerted = _policy.Alerted;

        _policy.Evaluate([State(15, ChargeState.Charging)]);

        Assert.Single(alerted);
        Assert.Empty(_policy.Alerted);
    }
}
