using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Vendors.SteelSeries;

namespace BatteryInfo.Core.Tests.Vendors.SteelSeries;

public class SteelSeriesProtocolTests
{
    // Observed on this PC (docs/protocols/steelseries.md section 5): Nova 5X connected, 45 %, on battery.
    internal static readonly byte[] OnlineReply = Pad65(0x00, 0xB0, 0x03, 0x02, 0x2D, 0x03, 0x64, 0x64);

    internal static byte[] Pad65(params byte[] head)
    {
        var report = new byte[65];
        head.CopyTo(report, 0);
        return report;
    }

    private static byte[] WithByte(byte[] report, int index, byte value)
    {
        var copy = (byte[])report.Clone();
        copy[index] = value;
        return copy;
    }

    internal static HidDeviceInfo Device(ushort pid = 0x2253, ushort usagePage = 0xFFC0, ushort usage = 0x0001,
        int input = 65, int output = 65, ushort vid = 0x1038, bool accessible = true) =>
        new($@"\\?\hid#vid_{vid:x4}&pid_{pid:x4}&mi_03#9&37215803&0&0000#{{4d1e55b2-f16f-11cf-88cb-001111000030}}",
            vid, pid, 0x0100, usagePage, usage, input, output, 515, 3, null, null, null, null, 0x00, 0x00, accessible);

    [Fact]
    public void Status_request_is_00_B0_and_63_zero_bytes()
    {
        var request = SteelSeriesProtocol.BuildStatusRequest();

        Assert.Equal(65, request.Length);
        Assert.Equal(0x00, request[0]);
        Assert.Equal(0xB0, request[1]);
        Assert.DoesNotContain(request[2..], b => b != 0);
    }

    [Fact]
    public void Connected_reply_gives_percent_and_discharging()
    {
        Assert.True(SteelSeriesProtocol.TryParseStatus(OnlineReply, out var status));
        Assert.Equal(new SteelSeriesStatus(true, 45, ChargeState.Discharging), status);
    }

    [Fact]
    public void Charging_flag_gives_charging() =>
        Assert.True(SteelSeriesProtocol.TryParseStatus(WithByte(OnlineReply, 5, 0x01), out var status)
            && status.Charge == ChargeState.Charging);

    [Fact]
    public void Link_byte_02_means_the_headset_is_off()
    {
        Assert.True(SteelSeriesProtocol.TryParseStatus(WithByte(OnlineReply, 2, 0x02), out var status));
        Assert.False(status.Connected);
    }

    [Fact]
    public void Percent_above_100_is_clamped() =>
        Assert.True(SteelSeriesProtocol.TryParseStatus(WithByte(OnlineReply, 4, 0xFF), out var status) && status.Percent == 100);

    [Theory]
    [InlineData(0, 0x01)] // another report id
    [InlineData(1, 0xB1)] // another command
    public void Other_frames_are_skipped(int index, byte value) =>
        Assert.False(SteelSeriesProtocol.TryParseStatus(WithByte(OnlineReply, index, value), out _));

    [Fact]
    public void Short_frame_is_skipped() =>
        Assert.False(SteelSeriesProtocol.TryParseStatus(OnlineReply[..5], out _));

    [Fact]
    public void Status_collection_is_matched_on_caps()
    {
        Assert.True(SteelSeriesProtocol.IsStatusCollection(Device()));
        Assert.False(SteelSeriesProtocol.IsStatusCollection(Device(usagePage: 0xFF00)));
        Assert.False(SteelSeriesProtocol.IsStatusCollection(Device(usagePage: 0x000C, input: 5, output: 0)));
        Assert.False(SteelSeriesProtocol.IsStatusCollection(Device(input: 64)));
        Assert.False(SteelSeriesProtocol.IsStatusCollection(Device(accessible: false)));
    }
}
