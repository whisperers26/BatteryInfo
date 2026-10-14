using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Vendors.Betop;

namespace BatteryInfo.Core.Tests.Vendors.Betop;

public class BetopCommandPolicyTests
{
    private readonly BetopCommandPolicy _policy = new();

    private static HidDeviceInfo Device(ushort vid = 0x20BC, ushort pid = 0x518D, ushort usagePage = 0x00FF, ushort usage = 0x0003,
        int input = 64, int output = 64) =>
        new($@"\\?\hid#vid_{vid:x4}&pid_{pid:x4}&mi_01&col01#b&accf143&0&0000#{{4d1e55b2-f16f-11cf-88cb-001111000030}}",
            vid, pid, 0x0100, usagePage, usage, input, output, 0, 1, null, null, null, null, 0x02, null, Accessible: true);

    private static byte[] Frame(params byte[] head) => BetopProtocolTests.Pad64(head);

    [Theory]
    [InlineData(0x15)]
    [InlineData(0x55)]
    [InlineData(0x18)]
    public void Read_queries_with_zero_payload_are_allowed_on_the_dongle(byte command) =>
        Assert.True(_policy.IsAllowed(Device(), Frame(0x02, command)));

    [Fact]
    public void Battery_query_is_allowed_on_the_wired_pad() =>
        Assert.True(_policy.IsAllowed(Device(pid: 0x518C), Frame(0x02, 0x15)));

    [Fact]
    public void Battery_query_is_allowed_on_a_plain_byte_sibling() =>
        Assert.True(_policy.IsAllowed(Device(pid: 0x514A), Frame(0x02, 0x15)));

    [Fact]
    public void Built_requests_pass_the_policy()
    {
        Assert.True(_policy.IsAllowed(Device(), BetopProtocol.BuildBatteryRequest()));
        Assert.True(_policy.IsAllowed(Device(), BetopProtocol.BuildRequest(BetopProtocol.GetFirmwareInfo)));
        Assert.True(_policy.IsAllowed(Device(), BetopProtocol.BuildRequest(BetopProtocol.GetRemoteInfo)));
    }

    // Section 7 of the protocol doc: every one of these shares report id 2 and must never be sent.
    [Theory]
    [InlineData(new byte[] { 0x02, 0x14, 0x01, 0x00, 0x00, 0x00, 0x01 })] // ENTER FIRMWARE UPDATE: dongle reboots to 20DD
    [InlineData(new byte[] { 0x02, 0x14 })]
    [InlineData(new byte[] { 0x02, 0x11 })] // syncMode
    [InlineData(new byte[] { 0x02, 0x21 })] // ping/sync with app-channel bits
    [InlineData(new byte[] { 0x02, 0x31 })] // exitMode
    [InlineData(new byte[] { 0x02, 0x12 })] // profile reads
    [InlineData(new byte[] { 0x02, 0x52 })]
    [InlineData(new byte[] { 0x02, 0x92 })]
    [InlineData(new byte[] { 0x02, 0x22 })] // profile writes
    [InlineData(new byte[] { 0x02, 0x62 })]
    [InlineData(new byte[] { 0x02, 0xA2 })]
    [InlineData(new byte[] { 0x02, 0x13 })] // saveAppConfig
    [InlineData(new byte[] { 0x02, 0x23 })] // reloadAppConfig
    [InlineData(new byte[] { 0x02, 0x33 })] // factory restore
    [InlineData(new byte[] { 0x02, 0x16 })] // vibrate
    [InlineData(new byte[] { 0x02, 0x36 })] // setFuncReset
    [InlineData(new byte[] { 0x02, 0x17 })] // enterTestMode
    [InlineData(new byte[] { 0x02, 0x27 })] // MAC get/set share a header
    [InlineData(new byte[] { 0x02, 0x37 })]
    [InlineData(new byte[] { 0x02, 0x25 })] // getKeyEvent
    [InlineData(new byte[] { 0x02, 0x35 })] // getTestData
    [InlineData(new byte[] { 0x02, 0x45 })] // getTestDataExt
    [InlineData(new byte[] { 0x02, 0x00 })]
    public void Dangerous_neighbours_are_refused(byte[] head) =>
        Assert.False(_policy.IsAllowed(Device(), Frame(head)));

    [Theory]
    [InlineData(0x15, 2)]
    [InlineData(0x55, 16)]
    [InlineData(0x18, 63)]
    public void A_read_with_any_payload_is_refused(byte command, int index)
    {
        var frame = Frame(0x02, command);
        frame[index] = 0x01;

        Assert.False(_policy.IsAllowed(Device(), frame));
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x15 })]
    [InlineData(new byte[] { 0x00, 0x15 })]
    public void Other_report_ids_are_refused(byte[] head) =>
        Assert.False(_policy.IsAllowed(Device(), Frame(head)));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(63)]
    [InlineData(65)]
    public void Frames_that_are_not_64_bytes_are_refused(int length)
    {
        var frame = new byte[length];
        if (length > 1)
        {
            frame[0] = 0x02;
            frame[1] = 0x15;
        }

        Assert.False(_policy.IsAllowed(Device(), frame));
    }

    [Fact]
    public void The_asleep_dongle_20DD_is_never_written_to() =>
        Assert.False(_policy.IsAllowed(Device(vid: 0x20DD), Frame(0x02, 0x15)));

    [Fact]
    public void The_KP50_firmware_update_device_0517_is_never_written_to() =>
        Assert.False(_policy.IsAllowed(Device(vid: 0x0517, pid: 0x5350), Frame(0x02, 0x15)));

    [Theory]
    [InlineData(0x5126)] // KP20A: SetHidWriteEnc 1, would see a substituted command
    [InlineData(0x5154)] // KP40D-b1: SetHidWriteEnc 1
    [InlineData(0x1234)]
    public void Pids_outside_the_plain_byte_table_are_refused(ushort pid) =>
        Assert.False(_policy.IsAllowed(Device(pid: pid), Frame(0x02, 0x15)));

    [Fact]
    public void Non_vendor_collections_of_the_dongle_are_refused()
    {
        Assert.False(_policy.IsAllowed(Device(usagePage: 0x0001, usage: 0x0006, input: 27, output: 2), Frame(0x02, 0x15)));
        Assert.False(_policy.IsAllowed(Device(usagePage: 0xFF00), Frame(0x02, 0x15)));
    }
}
