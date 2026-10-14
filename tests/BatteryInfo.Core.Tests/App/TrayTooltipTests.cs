using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public class TrayTooltipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static DeviceState State(string name, DeviceStatus status, params BatteryComponent[] components)
    {
        var reading = new DeviceReading("x:" + name, "X", name, DeviceKind.Mouse, status, components, Now, "test");
        return new DeviceState(reading, reading.HasBattery ? reading : null);
    }

    private static BatteryComponent One(int percent, ChargeState charge = ChargeState.Discharging) => BatteryComponent.Single(percent, charge);

    [Fact]
    public void No_devices()
    {
        Assert.Equal("BatteryInfo\nNo devices found", TrayTooltip.Build([]));
    }

    [Fact]
    public void One_line_per_device_under_a_header()
    {
        var text = TrayTooltip.Build(
        [
            State("PRO X2 SUPERSTRIKE", DeviceStatus.Online, One(56)),
            State("ROG Azoth X", DeviceStatus.Online, One(45, ChargeState.Charging)),
        ]);

        Assert.Equal("BatteryInfo\nPRO X2 SUPERSTRIKE: 56%\nROG Azoth X: 45% charging", text);
    }

    [Fact]
    public void Earbuds_approximate_and_unavailable_devices()
    {
        var approximate = State("FreeBuds", DeviceStatus.Online, One(80));
        approximate = approximate with { Latest = approximate.Latest with { Approximate = true } };

        var text = TrayTooltip.Build(
        [
            State("FreeClip", DeviceStatus.Online, new("Left", 76, ChargeState.Discharging), new("Right", 75, ChargeState.Discharging), new("Case", 100, ChargeState.Charging)),
            approximate,
            State("Pad", DeviceStatus.Asleep),
            State("Cetra", DeviceStatus.Offline, One(64)),
            State("Razer", DeviceStatus.NeedsAccess),
            State("Broken", DeviceStatus.Error),
        ]);

        Assert.Equal("BatteryInfo\nFreeClip: L76% R75% C100%\nFreeBuds: ≈80%\nPad: asleep\nCetra: not connected\nRazer: needs access\nBroken: error", text);
    }

    [Fact]
    public void Too_long_drops_the_header_then_shortens_names_but_keeps_every_value()
    {
        var states = new[]
        {
            State("PRO X2 SUPERSTRIKE", DeviceStatus.Online, One(18)),
            State("ROG Azoth X", DeviceStatus.Online, One(45, ChargeState.Charging)),
            State("HUAWEI FreeClip 2", DeviceStatus.Online, new("Left", 76, ChargeState.Discharging), new("Right", 75, ChargeState.Discharging), new("Case", 100, ChargeState.Discharging)),
            State("Betop KP70C2", DeviceStatus.Asleep),
            State("Razer Basilisk V3 Pro 35K", DeviceStatus.Online, One(96)),
            State("ROG Cetra Open Wireless", DeviceStatus.Offline, One(64)),
        };

        var text = TrayTooltip.Build(states);

        Assert.True(text.Length <= TrayTooltip.MaxLength, $"{text.Length}: {text}");
        Assert.DoesNotContain("BatteryInfo", text);
        var lines = text.Split('\n');
        Assert.Equal(6, lines.Length);
        Assert.EndsWith(": 18%", lines[0]);
        Assert.EndsWith(": L76% R75% C100%", lines[2]);
        Assert.EndsWith(": not connected", lines[5]);
        Assert.Contains("…", text);
    }

    [Fact]
    public void Far_too_many_devices_end_with_a_count_of_the_rest()
    {
        var states = Enumerable.Range(1, 30).Select(i => State($"Device number {i:00}", DeviceStatus.Online, One(50))).ToArray();

        var text = TrayTooltip.Build(states);

        Assert.True(text.Length <= TrayTooltip.MaxLength, $"{text.Length}: {text}");
        Assert.Matches(@"\n\+\d+ more$", text);
    }
}
