using BatteryInfo.Core.Model;
using BatteryInfo.Core.Vendors.Logitech;

namespace BatteryInfo.Core.Tests.Vendors.Logitech;

// Battery feature decoding, docs/protocols/logitech.md §7.
public class HidppBatteryTests
{
    private static readonly UnifiedBatteryCapabilities ThisMouse = new(SupportedLevels: 0x0F, Flags: 0x0F);

    private static byte[] Long(string hex)
    {
        var report = new byte[HidppProtocol.LongLength];
        Convert.FromHexString(hex.Replace(" ", "")).CopyTo(report, 0);
        return report;
    }

    // 0x1004 UNIFIED_BATTERY

    [Fact]
    public void Unified_capabilities_of_this_mouse_support_state_of_charge()
    {
        var caps = HidppProtocol.ParseUnifiedCapabilities(Long("11 01 06 0E 0F 0F 02 00"));

        Assert.Equal(ThisMouse, caps);
        Assert.True(caps.HasStateOfCharge);
    }

    [Fact]
    public void Captured_unified_status_is_56_percent_good_discharging()
    {
        var reply = Long("11 01 06 1E 38 04 00 00 00 00");

        Assert.Equal(new UnifiedBatteryStatus(56, UnifiedBatteryLevel.Good, 0), HidppProtocol.ParseUnifiedStatus(reply));
        Assert.Equal(new HidppBattery(56, ChargeState.Discharging, Approximate: false), HidppProtocol.DecodeUnifiedBattery(reply, ThisMouse));
    }

    [Theory]
    [InlineData(0, ChargeState.Discharging)]
    [InlineData(1, ChargeState.Charging)]
    [InlineData(2, ChargeState.Charging)]
    [InlineData(3, ChargeState.Full)]
    [InlineData(4, ChargeState.Fault)]
    [InlineData(5, ChargeState.Charging)]
    [InlineData(6, ChargeState.Unknown)]
    [InlineData(0xFF, ChargeState.Unknown)]
    public void Unified_charging_status_maps_to_charge_state(int status, ChargeState expected)
    {
        Assert.Equal(expected, HidppProtocol.UnifiedCharge((byte)status));
        Assert.Equal(expected, HidppProtocol.DecodeUnifiedBattery(Long($"11 01 06 1C 38 04 {status:X2} 00"), ThisMouse).Charge);
    }

    [Theory]
    [InlineData(0x08, 90)]
    [InlineData(0x04, 50)]
    [InlineData(0x02, 20)]
    [InlineData(0x01, 5)]
    [InlineData(0x0C, 90)]
    public void Without_state_of_charge_the_level_approximates_the_percent(int level, int percent)
    {
        var noSoc = new UnifiedBatteryCapabilities(0x0F, 0x01);

        var battery = HidppProtocol.DecodeUnifiedBattery(Long($"11 01 06 1C 00 {level:X2} 00 00"), noSoc);

        Assert.Equal(new HidppBattery(percent, ChargeState.Discharging, Approximate: true), battery);
    }

    [Fact]
    public void Unknown_level_gives_no_percent_rather_than_zero()
    {
        var noSoc = new UnifiedBatteryCapabilities(0x0F, 0x01);

        Assert.Null(HidppProtocol.DecodeUnifiedBattery(Long("11 01 06 1C 00 00 00 00"), noSoc).Percent);
    }

    [Fact]
    public void Levels_the_device_does_not_support_are_masked_out()
    {
        var criticalAndLowOnly = new UnifiedBatteryCapabilities(0x03, 0x01);

        Assert.Null(HidppProtocol.DecodeUnifiedBattery(Long("11 01 06 1C 00 04 00 00"), criticalAndLowOnly).Percent);
    }

    [Fact]
    public void State_of_charge_above_100_falls_back_to_the_level()
    {
        Assert.Equal(new HidppBattery(50, ChargeState.Discharging, true),
            HidppProtocol.DecodeUnifiedBattery(Long("11 01 06 1C C8 04 00 00"), ThisMouse));
    }

    [Fact]
    public void Without_capabilities_a_plausible_state_of_charge_is_used()
    {
        Assert.Equal(56, HidppProtocol.DecodeUnifiedBattery(Long("11 01 06 1C 38 04 00 00"), null).Percent);
        Assert.Equal(new HidppBattery(50, ChargeState.Discharging, true),
            HidppProtocol.DecodeUnifiedBattery(Long("11 01 06 1C 00 04 00 00"), null));
    }

    // 0x1000 BATTERY_STATUS

    [Theory]
    [InlineData("32 1E 00", 50, ChargeState.Discharging)]
    [InlineData("32 1E 01", 50, ChargeState.Charging)]
    [InlineData("5A 64 02", 90, ChargeState.Charging)]
    [InlineData("5A 64 03", 100, ChargeState.Full)]
    [InlineData("32 1E 04", 50, ChargeState.Charging)]
    [InlineData("32 1E 05", 50, ChargeState.Fault)]
    [InlineData("32 1E 06", 50, ChargeState.Fault)]
    [InlineData("32 1E 07", 50, ChargeState.Fault)]
    [InlineData("32 1E 08", 50, ChargeState.Unknown)]
    public void Battery_status_decodes_level_and_status(string payload, int percent, ChargeState charge)
    {
        Assert.Equal(new HidppBattery(percent, charge, false), HidppProtocol.DecodeBatteryStatus(Long($"11 01 05 0C {payload}")));
    }

    [Fact]
    public void Battery_status_level_0_is_unknown()
    {
        Assert.Null(HidppProtocol.DecodeBatteryStatus(Long("11 01 05 0C 00 00 00")).Percent);
    }

    // 0x1001 BATTERY_VOLTAGE

    [Theory]
    [InlineData(5000, 100)]
    [InlineData(4186, 100)]
    [InlineData(4185, 99)]
    [InlineData(4000, 81)]
    [InlineData(3537, 1)]
    [InlineData(3536, 0)]
    [InlineData(0, 0)]
    public void Voltage_uses_the_kernel_table(int millivolts, int percent)
    {
        Assert.Equal(percent, HidppProtocol.VoltageToPercent(millivolts));
    }

    [Theory]
    [InlineData(0x00, ChargeState.Discharging)]
    [InlineData(0x80, ChargeState.Charging)]
    [InlineData(0x81, ChargeState.Full)]
    [InlineData(0x82, ChargeState.NotCharging)]
    [InlineData(0x87, ChargeState.Unknown)]
    [InlineData(0x98, ChargeState.Charging)]
    public void Voltage_flags_give_the_charge_state(int flags, ChargeState charge)
    {
        Assert.Equal(new HidppBattery(81, charge, Approximate: true), HidppProtocol.DecodeBatteryVoltage(Long($"11 01 05 0C 0F A0 {flags:X2}")));
    }

    // 0x1F20 ADC_MEASUREMENT

    [Theory]
    [InlineData(4030, 100)]
    [InlineData(3900, 87)]
    [InlineData(3399, 1)]
    [InlineData(3398, 0)]
    [InlineData(0, 0)]
    public void Adc_voltage_uses_the_1f20_table(int millivolts, int percent)
    {
        Assert.Equal(percent, HidppProtocol.AdcVoltageToPercent(millivolts));
    }

    [Theory]
    [InlineData(0x01, ChargeState.Discharging)]
    [InlineData(0x03, ChargeState.Charging)]
    [InlineData(0x07, ChargeState.Full)]
    [InlineData(0x0F, ChargeState.Unknown)]
    public void Adc_flags_give_the_charge_state(int flags, ChargeState charge)
    {
        Assert.Equal(new HidppBattery(87, charge, Approximate: true), HidppProtocol.DecodeAdcMeasurement(Long($"11 01 05 0C 0F 3C {flags:X2}")));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x02)]
    public void Adc_flags_without_bit_0_mean_the_device_is_inactive(int flags)
    {
        Assert.Null(HidppProtocol.DecodeAdcMeasurement(Long($"11 01 05 0C 0F 3C {flags:X2}")));
    }

    // HID++ 1.0 registers

    [Theory]
    [InlineData(0x30, ChargeState.Discharging)]
    [InlineData(0x50, ChargeState.Charging)]
    [InlineData(0x90, ChargeState.Full)]
    [InlineData(0x10, ChargeState.Unknown)]
    public void Register_0d_gives_percent_and_status(int status, ChargeState charge)
    {
        Assert.Equal(new HidppBattery(75, charge, false), HidppProtocol.DecodeRegisterBatteryCharge(Convert.FromHexString($"1001810D4B00{status:X2}")));
    }

    [Theory]
    [InlineData(7, 0x00, 90, ChargeState.Discharging)]
    [InlineData(5, 0x00, 50, ChargeState.Discharging)]
    [InlineData(3, 0x21, 20, ChargeState.Charging)]
    [InlineData(1, 0x22, 5, ChargeState.Full)]
    [InlineData(5, 0x10, 50, ChargeState.Unknown)]
    public void Register_07_gives_an_approximate_level(int level, int charging, int percent, ChargeState charge)
    {
        var reply = new byte[] { 0x10, 0x01, 0x81, 0x07, (byte)level, (byte)charging, 0x00 };

        Assert.Equal(new HidppBattery(percent, charge, true), HidppProtocol.DecodeRegisterBatteryStatus(reply));
    }

    [Fact]
    public void Register_07_unknown_level_has_no_percent()
    {
        Assert.Null(HidppProtocol.DecodeRegisterBatteryStatus([0x10, 0x01, 0x81, 0x07, 0x00, 0x00, 0x00]).Percent);
    }
}
