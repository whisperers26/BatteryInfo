using BatteryInfo.Core.Vendors.Rog;

namespace BatteryInfo.Core.Tests.Vendors.Rog;

public class RogProtocolTests
{
    // Pads a captured report to the OMNI's 64-byte input length, as ReadFile returns it.
    private static byte[] Report(params byte[] bytes)
    {
        var report = new byte[64];
        bytes.CopyTo(report, 0);
        return report;
    }

    [Fact]
    public void Request_is_a_full_output_report_with_header_and_zero_payload()
    {
        var request = RogProtocol.BuildRequest(64, 0x02, RogProtocol.KeyboardPowerInfo);

        Assert.Equal(64, request.Length);
        Assert.Equal(new byte[] { 0x02, 0x12, 0x01, 0x00, 0x00 }, request[..5]);
        Assert.All(request[5..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Mouse_exist_request_carries_index_2()
    {
        var request = RogProtocol.BuildRequest(64, 0x03, RogProtocol.MouseExistInfo);

        Assert.Equal(new byte[] { 0x03, 0x12, 0x00, 0x02, 0x00 }, request[..5]);
    }

    [Fact]
    public void Report_id_0_request_is_65_bytes_starting_with_0()
    {
        var request = RogProtocol.BuildRequest(65, 0x00, RogProtocol.MousePowerInfo);

        Assert.Equal(65, request.Length);
        Assert.Equal(new byte[] { 0x00, 0x12, 0x07, 0x00, 0x00 }, request[..5]);
    }

    [Fact]
    public void Cetra_request_uses_report_CC()
    {
        var request = RogProtocol.BuildRequest(64, RogProtocol.CetraReportId, RogProtocol.CetraBatteryLevel);

        Assert.Equal(new byte[] { 0xCC, 0x12, 0x12, 0x00, 0x00 }, request[..5]);
    }

    [Fact]
    public void Request_shorter_than_the_header_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RogProtocol.BuildRequest(4, 0x02, RogProtocol.KeyboardPowerInfo));
    }

    [Fact]
    public void Scan_reply_matches_on_command_only_and_lists_the_azoth_x_slot()
    {
        var reply = Report(0x01, 0xA0, 0x00, 0x01, 0x00, 0x25, 0x1C, 0x02, 0x04);

        Assert.Equal(RogFrame.Reply, RogProtocol.Classify(reply, 0x01, RogProtocol.ScanForPairedDevices));
        var slot = Assert.Single(RogProtocol.ParseScan(reply));
        Assert.Equal(new RogSlot(0x1C25, 0x02, 0x04), slot);
    }

    [Fact]
    public void Scan_with_no_paired_devices_is_empty()
    {
        Assert.Empty(RogProtocol.ParseScan(Report(0x01, 0xA0, 0x00, 0x00, 0x00)));
    }

    [Fact]
    public void Scan_count_beyond_the_report_is_truncated()
    {
        var reply = new byte[] { 0x01, 0xA0, 0x00, 0x05, 0x00, 0x25, 0x1C, 0x02, 0x04, 0xCE, 0x1A };

        Assert.Single(RogProtocol.ParseScan(reply));
    }

    [Fact]
    public void Scan_lists_keyboard_and_mouse_slots()
    {
        var reply = Report(0x01, 0xA0, 0x00, 0x02, 0x00, 0x25, 0x1C, 0x02, 0x04, 0xB0, 0x1A, 0x03, 0x05);

        Assert.Equal([new RogSlot(0x1C25, 2, 4), new RogSlot(0x1AB0, 3, 5)], RogProtocol.ParseScan(reply));
    }

    [Fact]
    public void Reply_must_echo_report_id_command_sub_and_index()
    {
        var reply = Report(0x02, 0x12, 0x01, 0x00, 0x00, 0x00, 0x2D);

        Assert.Equal(RogFrame.Reply, RogProtocol.Classify(reply, 0x02, RogProtocol.KeyboardPowerInfo));
        Assert.Equal(RogFrame.Unrelated, RogProtocol.Classify(reply, 0x03, RogProtocol.KeyboardPowerInfo));
        Assert.Equal(RogFrame.Unrelated, RogProtocol.Classify(reply, 0x02, RogProtocol.KeyboardConnectionType));
        Assert.Equal(RogFrame.Unrelated, RogProtocol.Classify(Report(0x03, 0x12, 0x00, 0x00, 0x00), 0x03, RogProtocol.MouseExistInfo));
    }

    [Fact]
    public void Scan_reply_with_a_different_report_id_is_unrelated()
    {
        Assert.Equal(RogFrame.Unrelated, RogProtocol.Classify(Report(0x02, 0xA0, 0x00, 0x01, 0x00), 0x01, RogProtocol.ScanForPairedDevices));
    }

    [Fact]
    public void FF_AA_is_a_reject()
    {
        Assert.Equal(RogFrame.Rejected, RogProtocol.Classify(Report(0x02, 0xFF, 0xAA), 0x02, RogProtocol.KeyboardPowerInfo));
        Assert.Equal(RogFrame.Rejected, RogProtocol.Classify(new byte[] { 0x03, 0xFF, 0xAA }, 0x03, RogProtocol.MousePowerInfo));
    }

    [Fact]
    public void Reject_on_another_report_id_is_unrelated()
    {
        Assert.Equal(RogFrame.Unrelated, RogProtocol.Classify(Report(0x03, 0xFF, 0xAA), 0x02, RogProtocol.KeyboardPowerInfo));
    }

    [Fact]
    public void Truncated_frames_are_unrelated()
    {
        Assert.Equal(RogFrame.Unrelated, RogProtocol.Classify(new byte[] { 0x02, 0x12, 0x01 }, 0x02, RogProtocol.KeyboardPowerInfo));
        Assert.Equal(RogFrame.Unrelated, RogProtocol.Classify([], 0x02, RogProtocol.KeyboardPowerInfo));
    }

    [Fact]
    public void Serial_number_is_ascii_with_nuls_stripped()
    {
        var reply = Report(0x01, 0xA1, 0x00, 0x00, 0x00, 0x54, 0x34, 0x4D, 0x50, 0x4B, 0x52, 0x44, 0x30, 0x31, 0x54, 0x48, 0x43);

        Assert.Equal("T4MPKRD01THC", RogProtocol.ParseSerial(reply));
    }

    [Fact]
    public void Blank_or_non_printable_serial_is_null()
    {
        Assert.Null(RogProtocol.ParseSerial(Report(0x01, 0xA1, 0x00, 0x00, 0x00)));
        Assert.Null(RogProtocol.ParseSerial(Report(0x01, 0xA1, 0x00, 0x00, 0x00, 0x41, 0x0A, 0x42)));
    }

    [Theory]
    [InlineData(0x00, RogConnectionType.Wired)]
    [InlineData(0x01, RogConnectionType.Rf)]
    [InlineData(0x02, RogConnectionType.Bluetooth)]
    [InlineData(0xFF, RogConnectionType.NotPresent)]
    public void Connection_type_is_d4(byte value, RogConnectionType expected)
    {
        Assert.Equal(expected, RogProtocol.ParseConnectionType(Report(0x02, 0x12, 0x03, 0x00, 0x00, value)));
    }

    [Fact]
    public void Connection_type_from_a_short_reply_is_null()
    {
        Assert.Null(RogProtocol.ParseConnectionType(new byte[] { 0x02, 0x12, 0x03, 0x00, 0x00 }));
    }

    [Fact]
    public void Keyboard_power_45_percent_discharging()
    {
        // d[5] = 0x2D (45 %), d[6] idle 3, d[8] = 0 not charging, d[10] percent again, d[11..12] = 3786 mV.
        var reply = Report(0x02, 0x12, 0x01, 0x00, 0x00, 0x00, 0x2D, 0x03, 0x00, 0x00, 0x14, 0x2D, 0xCA, 0x0E);

        Assert.Equal(new RogPower(45, false), RogProtocol.ParseKeyboardPower(reply));
    }

    [Fact]
    public void Keyboard_power_charging_flag_is_d8()
    {
        var reply = Report(0x02, 0x12, 0x01, 0x00, 0x00, 0x00, 0x2D, 0x03, 0x00, 0x01, 0x14);

        Assert.Equal(new RogPower(45, true), RogProtocol.ParseKeyboardPower(reply));
    }

    [Fact]
    public void Keyboard_power_unknown_charging_value_is_null()
    {
        var reply = Report(0x02, 0x12, 0x01, 0x00, 0x00, 0x00, 0x2D, 0x03, 0x00, 0x07, 0x14);

        Assert.Equal(new RogPower(45, null), RogProtocol.ParseKeyboardPower(reply));
    }

    [Fact]
    public void Keyboard_percent_above_100_is_unknown()
    {
        var reply = Report(0x02, 0x12, 0x01, 0x00, 0x00, 0x00, 0x65, 0x03, 0x00, 0x00, 0x14);

        Assert.Equal(new RogPower(null, false), RogProtocol.ParseKeyboardPower(reply));
    }

    [Fact]
    public void Keyboard_power_needs_10_data_bytes()
    {
        Assert.Null(RogProtocol.ParseKeyboardPower(new byte[] { 0x02, 0x12, 0x01, 0x00, 0x00, 0x00, 0x2D, 0x03, 0x00, 0x00 }));
        Assert.NotNull(RogProtocol.ParseKeyboardPower(new byte[] { 0x02, 0x12, 0x01, 0x00, 0x00, 0x00, 0x2D, 0x03, 0x00, 0x00, 0x14 }));
    }

    [Fact]
    public void Mouse_exist_is_d4_equal_1()
    {
        Assert.True(RogProtocol.ParseMouseExists(Report(0x03, 0x12, 0x00, 0x02, 0x00, 0x01)));
        Assert.False(RogProtocol.ParseMouseExists(Report(0x03, 0x12, 0x00, 0x02, 0x00, 0x00)));
        Assert.Null(RogProtocol.ParseMouseExists(new byte[] { 0x03, 0x12, 0x00, 0x02, 0x00 }));
    }

    [Fact]
    public void Mouse_power_percent_d4_and_charging_d9()
    {
        // d[4] = 80 %, d[5] sleep, d[6] threshold, d[7..8] voltage, d[9] charging, d[10] battery type.
        var reply = Report(0x03, 0x12, 0x07, 0x00, 0x00, 0x50, 0x02, 0x0A, 0xD0, 0x0E, 0x01, 0x00);

        Assert.Equal(new RogPower(80, true), RogProtocol.ParseMousePower(reply));
    }

    [Fact]
    public void Mouse_power_not_charging()
    {
        var reply = Report(0x03, 0x12, 0x07, 0x00, 0x00, 0x50, 0x02, 0x0A, 0xD0, 0x0E, 0x00, 0x00);

        Assert.Equal(new RogPower(80, false), RogProtocol.ParseMousePower(reply));
    }

    [Fact]
    public void Mouse_power_zero_and_not_charging_means_asleep_not_0_percent()
    {
        var reply = Report(0x03, 0x12, 0x07, 0x00, 0x00, 0x00, 0x02, 0x0A, 0x00, 0x00, 0x00, 0x00);

        Assert.Equal(new RogPower(null, null, Asleep: true), RogProtocol.ParseMousePower(reply));
    }

    [Fact]
    public void Mouse_power_needs_11_data_bytes()
    {
        Assert.Null(RogProtocol.ParseMousePower(new byte[] { 0x03, 0x12, 0x07, 0x00, 0x00, 0x50, 0x02, 0x0A, 0xD0, 0x0E, 0x00 }));
    }

    [Fact]
    public void Cetra_battery_is_left_right_case()
    {
        var reply = Report(0xCC, 0x12, 0x12, 0x00, 0x00, 0x4B, 0x4C, 0x64);

        Assert.Equal(RogFrame.Reply, RogProtocol.Classify(reply, RogProtocol.CetraReportId, RogProtocol.CetraBatteryLevel));
        Assert.Equal(new RogCetraBattery(75, 76, 100), RogProtocol.ParseCetraBattery(reply));
    }

    [Fact]
    public void Cetra_battery_above_100_is_unknown()
    {
        var reply = Report(0xCC, 0x12, 0x12, 0x00, 0x00, 0x4B, 0xFF, 0x65);

        Assert.Equal(new RogCetraBattery(75, null, null), RogProtocol.ParseCetraBattery(reply));
    }

    [Theory]
    [InlineData(0x00, 0x00, false, false, false)]
    [InlineData(0x01, 0x00, true, false, false)]
    [InlineData(0x02, 0x00, false, true, false)]
    [InlineData(0x03, 0x01, true, true, true)]
    [InlineData(0x00, 0x01, false, false, true)]
    public void Cetra_charging_status(byte buds, byte @case, bool left, bool right, bool caseCharging)
    {
        var reply = Report(0xCC, 0x12, 0x11, 0x00, 0x00, buds, @case);

        Assert.Equal(new RogCetraCharging(left, right, caseCharging), RogProtocol.ParseCetraCharging(reply));
    }

    [Fact]
    public void Cetra_short_replies_are_null()
    {
        Assert.Null(RogProtocol.ParseCetraBattery(new byte[] { 0xCC, 0x12, 0x12, 0x00, 0x00, 0x4B, 0x4C }));
        Assert.Null(RogProtocol.ParseCetraCharging(new byte[] { 0xCC, 0x12, 0x11, 0x00, 0x00, 0x01 }));
    }
}
