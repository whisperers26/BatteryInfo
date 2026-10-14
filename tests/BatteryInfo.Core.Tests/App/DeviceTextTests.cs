using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public class DeviceTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 15, 30, 0, TimeSpan.FromHours(8));

    private static DeviceReading Mouse(DeviceStatus status, int? percent, ChargeState charge = ChargeState.Discharging,
        DateTimeOffset? at = null, bool approximate = false, string? detail = null) =>
        new("logitech:B4655832", "Logitech", "PRO X2 SUPERSTRIKE", DeviceKind.Mouse, status,
            percent is null ? [] : [BatteryComponent.Single(percent, charge)], at ?? Now, "HID++ 0x1004", approximate, detail);

    private static DeviceReading Earbuds(DeviceStatus status, ChargeState left, ChargeState right, ChargeState @case,
        DateTimeOffset? at = null) =>
        new("huawei:C0DA5EA83EC7", "HUAWEI", "HUAWEI FreeClip 2", DeviceKind.Earbuds, status,
            [new("Left", 76, left), new("Right", 75, right), new("Case", 100, @case)], at ?? Now, "SPP");

    /// <summary>Earbuds whose battery channel was busy, so the value is Windows' own HFP level.</summary>
    private static DeviceReading WindowsEarbuds(int percent) =>
        new("huawei:C0DA5EA83EC7", "HUAWEI", "HUAWEI FreeClip 2", DeviceKind.Earbuds, DeviceStatus.Online,
            [BatteryComponent.Single(percent, ChargeState.Unknown)], Now, DeviceReading.WindowsHfpSource, Approximate: true,
            Detail: "SPP busy; approximate value from Windows");

    private static DeviceState Fresh(DeviceReading reading) => new(reading, reading.HasBattery ? reading : null);

    [Theory]
    [InlineData(ChargeState.Discharging, "Connected")]
    [InlineData(ChargeState.Unknown, "Connected")]
    [InlineData(ChargeState.Charging, "Charging")]
    [InlineData(ChargeState.Full, "Fully charged")]
    [InlineData(ChargeState.NotCharging, "Plugged in, not charging")]
    [InlineData(ChargeState.Fault, "Charging problem")]
    public void Online_single_battery_shows_the_charge_state(ChargeState charge, string expected)
    {
        Assert.Equal(expected, DeviceText.StatusLine(Fresh(Mouse(DeviceStatus.Online, 56, charge)), Now));
    }

    [Fact]
    public void Online_earbuds_name_the_components_that_charge()
    {
        Assert.Equal("Connected", DeviceText.StatusLine(Fresh(Earbuds(DeviceStatus.Online,
            ChargeState.Discharging, ChargeState.Discharging, ChargeState.Discharging)), Now));
        Assert.Equal("Case charging", DeviceText.StatusLine(Fresh(Earbuds(DeviceStatus.Online,
            ChargeState.Discharging, ChargeState.Discharging, ChargeState.Charging)), Now));
        Assert.Equal("Left, Right charging", DeviceText.StatusLine(Fresh(Earbuds(DeviceStatus.Online,
            ChargeState.Charging, ChargeState.Charging, ChargeState.Discharging)), Now));
        Assert.Equal("Charging", DeviceText.StatusLine(Fresh(Earbuds(DeviceStatus.Online,
            ChargeState.Charging, ChargeState.Charging, ChargeState.Charging)), Now));
    }

    [Fact]
    public void Approximate_values_from_windows_say_so()
    {
        Assert.Equal("Approx. from Windows", DeviceText.StatusLine(Fresh(WindowsEarbuds(80)), Now));
    }

    [Fact]
    public void Approximate_values_read_from_the_device_are_estimated()
    {
        var fromVoltage = Mouse(DeviceStatus.Online, 81, approximate: true) with { Source = "HID++ 0x1001" };

        Assert.Equal("Estimated", DeviceText.StatusLine(Fresh(fromVoltage), Now));
        Assert.Equal("Estimated · Charging",
            DeviceText.StatusLine(Fresh(Mouse(DeviceStatus.Online, 50, ChargeState.Charging, approximate: true)), Now));
    }

    [Fact]
    public void Online_with_a_detail_shows_the_detail()
    {
        Assert.Equal("Timed out reading devices.",
            DeviceText.StatusLine(Fresh(Mouse(DeviceStatus.Online, 56, detail: "Timed out reading devices.")), Now));
    }

    [Fact]
    public void Asleep_with_a_stale_value_shows_value_and_age()
    {
        var earlier = Mouse(DeviceStatus.Online, 82, at: Now.AddMinutes(-14));
        var state = new DeviceState(Mouse(DeviceStatus.Asleep, null), earlier);

        Assert.Equal("Asleep · 82 % 14 min ago", DeviceText.StatusLine(state, Now));
    }

    [Fact]
    public void Asleep_earbuds_with_stale_values_show_only_the_age()
    {
        var earlier = Earbuds(DeviceStatus.Online, ChargeState.Discharging, ChargeState.Discharging, ChargeState.Discharging, Now.AddHours(-2));
        var asleep = earlier with { Status = DeviceStatus.Asleep, Components = [], Timestamp = Now };

        Assert.Equal("Asleep · last read 2 h ago", DeviceText.StatusLine(new DeviceState(asleep, earlier), Now));
    }

    [Fact]
    public void Asleep_without_any_value_is_just_asleep()
    {
        Assert.Equal("Asleep", DeviceText.StatusLine(Fresh(Mouse(DeviceStatus.Asleep, null)), Now));
    }

    [Fact]
    public void Offline_shows_the_last_value_and_when()
    {
        var offline = Mouse(DeviceStatus.Offline, 82, at: Now.AddDays(-1));

        Assert.Equal("Not connected · last 82 % yesterday", DeviceText.StatusLine(Fresh(offline), Now));
        Assert.Equal("Not connected", DeviceText.StatusLine(Fresh(Mouse(DeviceStatus.Offline, null)), Now));
    }

    [Fact]
    public void Needs_access_and_error_show_the_detail_or_a_fallback()
    {
        Assert.Equal("Install the Razer driver", DeviceText.StatusLine(Fresh(Mouse(DeviceStatus.NeedsAccess, null, detail: "Install the Razer driver")), Now));
        Assert.Equal("Windows denied access", DeviceText.StatusLine(Fresh(Mouse(DeviceStatus.NeedsAccess, null)), Now));
        Assert.Equal("Internal error: unsafe command refused", DeviceText.StatusLine(Fresh(Mouse(DeviceStatus.Error, null, detail: "Internal error: unsafe command refused")), Now));
        Assert.Equal("Could not read the battery", DeviceText.StatusLine(Fresh(Mouse(DeviceStatus.Error, null)), Now));
    }

    [Theory]
    [InlineData(56, false, "56 %")]
    [InlineData(80, true, "≈80 %")]
    [InlineData(null, false, "–")]
    [InlineData(null, true, "–")]
    public void Percent(int? percent, bool approximate, string expected)
    {
        Assert.Equal(expected, DeviceText.Percent(percent, approximate));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Left", "L")]
    [InlineData("Right", "R")]
    [InlineData("Case", "Case")]
    [InlineData("Dock", "Dock")]
    public void ComponentLabel(string name, string expected)
    {
        Assert.Equal(expected, DeviceText.ComponentLabel(name));
    }

    [Theory]
    [InlineData("", 18, "PRO X2 SUPERSTRIKE is at 18 %")]
    [InlineData("Left", 15, "PRO X2 SUPERSTRIKE left earbud is at 15 %")]
    [InlineData("Right", 15, "PRO X2 SUPERSTRIKE right earbud is at 15 %")]
    [InlineData("Case", 9, "PRO X2 SUPERSTRIKE case is at 9 %")]
    public void LowBatteryMessage(string component, int percent, string expected)
    {
        Assert.Equal(expected, DeviceText.LowBatteryMessage(new LowBatteryAlert("logitech:B4655832", "PRO X2 SUPERSTRIKE", component, percent)));
    }
}
