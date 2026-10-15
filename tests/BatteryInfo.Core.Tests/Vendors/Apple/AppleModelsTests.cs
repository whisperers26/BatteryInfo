using BatteryInfo.Core.Model;
using BatteryInfo.Core.Vendors.Apple;
using static BatteryInfo.Core.Tests.Vendors.Apple.AppleVectors;

namespace BatteryInfo.Core.Tests.Vendors.Apple;

public class AppleModelsTests
{
    private static ProximityStatus Parse(string hex) => AppleProximityProtocol.Parse(Bytes(hex))!.Value;

    private static ChargeState Charge(bool charging) => charging ? ChargeState.Charging : ChargeState.Unknown;

    [Theory]
    [InlineData(0x2014, "AirPods Pro 2", AppleLayout.Earbuds)]
    [InlineData(0x2024, "AirPods Pro 2 (USB-C)", AppleLayout.Earbuds)]
    [InlineData(0x2030, "AirPods 5", AppleLayout.Earbuds)]
    [InlineData(0x2036, "AirPods 5", AppleLayout.Earbuds)]
    [InlineData(0x201F, "AirPods Max (USB-C)", AppleLayout.OneBattery)]
    [InlineData(0x2026, "Beats Solo Buds", AppleLayout.EarbudsWithoutCaseBattery)]
    [InlineData(0x2010, "Beats Flex", AppleLayout.OneBattery)]
    public void Finds_a_model_by_its_pid(int pid, string name, AppleLayout layout)
    {
        var model = AppleModels.Find((ushort)pid);

        Assert.NotNull(model);
        Assert.Equal(name, model.Name);
        Assert.Equal(layout, model.Layout);
    }

    [Theory]
    [InlineData(0x201E)] // a case-bearing PID iOS knows, never captured
    [InlineData(0x0E20)] // the bytes 0E 20 read the wrong way round
    [InlineData(0x0000)]
    public void Unknown_pids_have_no_model(int pid) => Assert.Null(AppleModels.Find((ushort)pid));

    [Fact]
    public void Every_model_has_an_apple_audio_pid_and_one_entry()
    {
        Assert.All(AppleModels.All, m => Assert.Equal(0x20, m.Id >> 8));
        Assert.Equal(AppleModels.All.Count, AppleModels.All.Select(m => m.Id).Distinct().Count());
        Assert.Equal(31, AppleModels.All.Count);
    }

    [Fact]
    public void Only_the_airpods_max_report_charging_on_their_one_battery()
    {
        var charging = AppleModels.All.Where(m => m.Layout == AppleLayout.OneBattery && m.ReportsCharging).Select(m => m.Id);

        Assert.Equal([0x200A, 0x201F, 0x202D], charging.Order().Select(id => (int)id));
    }

    [Theory]
    [MemberData(nameof(EarbudNames), MemberType = typeof(AppleVectors))]
    public void Earbud_frames_become_left_right_and_case(string name)
    {
        var frame = EarbudFrames.Single(f => f.Name == name);
        var model = AppleModels.Find((ushort)frame.ModelId)!;

        var components = model.Components(Parse(frame.Hex));

        Assert.Equal(
            [
                new BatteryComponent("Left", frame.Left, Charge(frame.LeftCharging)),
                new BatteryComponent("Right", frame.Right, Charge(frame.RightCharging)),
                new BatteryComponent("Case", frame.Case, Charge(frame.CaseCharging)),
            ],
            components);
    }

    [Theory]
    [MemberData(nameof(OneBatteryNames), MemberType = typeof(AppleVectors))]
    public void One_battery_frames_become_a_single_component(string name)
    {
        var frame = OneBatteryFrames.Single(f => f.Name == name);
        var model = AppleModels.Find((ushort)frame.ModelId)!;

        var components = model.Components(Parse(frame.Hex));

        Assert.Equal([BatteryComponent.Single(frame.Level, Charge(frame.Charging))], components);
    }

    [Fact]
    public void A_one_battery_model_ignores_the_other_nibbles_that_read_zero()
    {
        // AirPods Max USB-C on Windows: Data[6] = 03 (30 %), the high nibble and the case nibble are 0, not 0 %.
        var frame = OneBatteryFrames.Single(f => f.Name == "Orbit_AirPodsMaxUsbc_37pct_Windows");

        var components = AppleModels.Find(0x201F)!.Components(Parse(frame.Hex));

        Assert.Equal([BatteryComponent.Single(30, ChargeState.Unknown)], components);
    }

    [Fact]
    public void Beats_one_battery_models_never_report_charging()
    {
        // The Beats Flex frame with the charging bit set by hand: capod reports no charging for these models.
        var status = Parse(OneBatteryFrames.Single(f => f.Name == "OpenPods105_BeatsFlex").Hex) with { ThisCharging = true };

        Assert.Equal([BatteryComponent.Single(40, ChargeState.Unknown)], AppleModels.Find(0x2010)!.Components(status));
    }

    [Fact]
    public void Solo_buds_have_no_case_battery()
    {
        var status = Parse(EarbudFrames[2].Hex) with { ModelId = 0x2026, CaseLevel = 0 };

        var components = AppleModels.Find(0x2026)!.Components(status);

        Assert.Equal(["Left", "Right"], components.Select(c => c.Name));
    }

    [Fact]
    public void Charging_is_not_shown_for_a_level_that_is_not_reported()
    {
        var status = Parse(EarbudFrames[0].Hex) with { OtherCharging = true }; // the left pod reports F

        var left = AppleModels.Find(0x200E)!.Components(status)[0];

        Assert.Equal(new BatteryComponent("Left", null, ChargeState.Unknown), left);
    }
}
