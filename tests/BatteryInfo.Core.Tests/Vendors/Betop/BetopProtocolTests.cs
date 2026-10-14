using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Vendors.Betop;

namespace BatteryInfo.Core.Tests.Vendors.Betop;

public class BetopProtocolTests
{
    // Observed three times on this PC (docs/protocols/betop.md section 4): 91 %, not charging.
    internal static readonly byte[] ObservedReply = Pad64(
        0x02, 0x15, 0x5B, 0x00, 0x50, 0x01, 0x00, 0x50, 0x01, 0x02, 0x00, 0xFF, 0xFF, 0x0B, 0xE8, 0xE7);

    internal static byte[] Pad64(params byte[] head)
    {
        var report = new byte[64];
        head.CopyTo(report, 0);
        return report;
    }

    private static byte[] WithByte(byte[] report, int index, byte value)
    {
        var copy = (byte[])report.Clone();
        copy[index] = value;
        return copy;
    }

    [Fact]
    public void Battery_request_is_02_15_and_62_zero_bytes()
    {
        var request = BetopProtocol.BuildBatteryRequest();

        Assert.Equal(Pad64(0x02, 0x15), request);
    }

    [Theory]
    [InlineData(BetopProtocol.GetFirmwareInfo, 0x55)]
    [InlineData(BetopProtocol.GetRemoteInfo, 0x18)]
    public void Optional_reads_use_the_same_frame_shape(byte command, byte expected) =>
        Assert.Equal(Pad64(0x02, expected), BetopProtocol.BuildRequest(command));

    [Fact]
    public void Observed_reply_is_91_percent_discharging()
    {
        Assert.True(BetopProtocol.TryParseBattery(ObservedReply, out var status));

        Assert.Equal(new BetopStatus(91, ChargeState.Discharging), status);
    }

    [Fact]
    public void Low_nibble_1_is_charging()
    {
        Assert.True(BetopProtocol.TryParseBattery(WithByte(ObservedReply, 4, 0x51), out var status));

        Assert.Equal(ChargeState.Charging, status.Charge);
    }

    [Theory]
    [InlineData(0x52)]
    [InlineData(0x0F)]
    public void Unobserved_low_nibbles_are_unknown(byte value)
    {
        Assert.True(BetopProtocol.TryParseBattery(WithByte(ObservedReply, 4, value), out var status));

        Assert.Equal(ChargeState.Unknown, status.Charge);
    }

    [Theory]
    [InlineData(0x00, ChargeState.Discharging)]
    [InlineData(0x01, ChargeState.Charging)]
    [InlineData(0x50, ChargeState.Discharging)]
    [InlineData(0x51, ChargeState.Charging)]
    [InlineData(0xF1, ChargeState.Charging)]
    public void The_high_nibble_of_the_charge_byte_is_ignored(byte value, ChargeState expected)
    {
        Assert.True(BetopProtocol.TryParseBattery(WithByte(ObservedReply, 4, value), out var status));

        Assert.Equal(expected, status.Charge);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(255, 100)]
    public void Percent_above_100_is_clamped(byte raw, int expected)
    {
        Assert.True(BetopProtocol.TryParseBattery(WithByte(ObservedReply, 2, raw), out var status));

        Assert.Equal(expected, status.Percent);
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0x25, 0x5B, 0x00, 0x50 })] // getKeyEvent reply (BetopGame)
    [InlineData(new byte[] { 0x02, 0x11, 0x5B, 0x00, 0x50 })] // syncMode reply (BetopGame)
    [InlineData(new byte[] { 0x02, 0x21, 0x5B, 0x00, 0x50 })] // ping/sync reply (BetopGame)
    [InlineData(new byte[] { 0x02, 0x55, 0x01, 0x00, 0x03 })] // unsolicited fw_conn change
    [InlineData(new byte[] { 0x01, 0x15, 0x5B, 0x00, 0x50 })] // other report id
    [InlineData(new byte[] { 0x02, 0x15, 0x5B, 0x00 })]       // too short to hold the charge byte
    [InlineData(new byte[0])]
    public void Other_frames_are_not_battery_replies(byte[] report) =>
        Assert.False(BetopProtocol.TryParseBattery(report, out _));

    private static HidDeviceInfo Collection(ushort usagePage = 0x00FF, ushort usage = 0x0003, int input = 64, int output = 64,
        bool accessible = true) =>
        new(@"\\?\hid#vid_20bc&pid_518d&mi_01&col01#b&accf143&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}",
            0x20BC, 0x518D, 0x0100, usagePage, usage, input, output, 0, 1, null, null, null, null, 0x02, null, accessible);

    [Fact]
    public void Vendor_collection_is_up_00FF_u_3_with_64_byte_reports() =>
        Assert.True(BetopProtocol.IsVendorCollection(Collection()));

    [Theory]
    [InlineData(0xFF00, 0x0003, 64, 64, true)] // the doc warns: 0x00FF, not 0xFF00
    [InlineData(0x00FF, 0x0001, 64, 64, true)]
    [InlineData(0x0001, 0x0006, 27, 2, true)] // COL03 keyboard
    [InlineData(0x0001, 0x0005, 15, 0, true)] // IG_00 XInput
    [InlineData(0x00FF, 0x0003, 64, 32, true)]
    [InlineData(0x00FF, 0x0003, 32, 64, true)]
    [InlineData(0x00FF, 0x0003, 64, 64, false)]
    public void Other_collections_are_not_the_vendor_collection(int usagePage, int usage, int input, int output, bool accessible) =>
        Assert.False(BetopProtocol.IsVendorCollection(Collection((ushort)usagePage, (ushort)usage, input, output, accessible)));
}
