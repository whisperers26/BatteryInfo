using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Rog;

namespace BatteryInfo.Core.Tests.Vendors.Rog;

public class RogCommandPolicyTests
{
    private readonly RogCommandPolicy _policy = new();
    private readonly FakeHidBackend _backend = new();

    private HidDeviceInfo Collection(ushort pid, ushort usagePage, byte? reportId, int output = 64, ushort vid = 0x0B05, ushort usage = 1) =>
        _backend.Add(vid, pid, usagePage, usage, output, output, mi: 2, outputReportId: reportId).Info;

    private HidDeviceInfo OmniReceiver => Collection(0x1ACE, 0xFF02, 0x01);
    private HidDeviceInfo OmniKeyboardSlot => Collection(0x1ACE, 0xFF00, 0x02);
    private HidDeviceInfo OmniMouseSlot => Collection(0x1ACE, 0xFF01, 0x03);
    private HidDeviceInfo Cetra => Collection(0x1CAC, 0xFF00, RogProtocol.CetraReportId);

    private static byte[] Frame(HidDeviceInfo device, params byte[] header)
    {
        var frame = new byte[device.OutputReportLength];
        header.CopyTo(frame, 0);
        return frame;
    }

    private bool Allowed(HidDeviceInfo device, params byte[] header) => _policy.IsAllowed(device, Frame(device, header));

    [Theory]
    [InlineData(0xA1, 0x01)]
    [InlineData(0xA1, 0x00)]
    [InlineData(0xA0, 0x00)]
    public void Receiver_reads_are_allowed_on_the_receiver_collection(byte command, byte sub)
    {
        Assert.True(Allowed(OmniReceiver, 0x01, command, sub, 0x00, 0x00));
    }

    [Theory]
    [InlineData(0x12, 0x03, 0x00)]
    [InlineData(0x12, 0x01, 0x00)]
    [InlineData(0x12, 0x00, 0x00)]
    [InlineData(0x12, 0x07, 0x00)]
    public void Keyboard_reads_are_allowed_on_the_keyboard_slot(byte command, byte sub, byte index)
    {
        Assert.True(Allowed(OmniKeyboardSlot, 0x02, command, sub, index, 0x00));
    }

    [Theory]
    [InlineData(0x12, 0x00, 0x02)]
    [InlineData(0x12, 0x00, 0x00)]
    [InlineData(0x12, 0x07, 0x00)]
    public void Mouse_reads_are_allowed_on_the_mouse_slot(byte command, byte sub, byte index)
    {
        Assert.True(Allowed(OmniMouseSlot, 0x03, command, sub, index, 0x00));
    }

    [Fact]
    public void Report_id_0_direct_keyboard_read_is_allowed()
    {
        var wired = Collection(0x1C24, 0xFF00, 0x00, output: 65);

        Assert.True(Allowed(wired, 0x00, 0x12, 0x01, 0x00, 0x00));
    }

    [Fact]
    public void Collection_without_an_output_report_id_counts_as_report_id_0()
    {
        var mouse = Collection(0x1960, 0xFF01, null, output: 65);

        Assert.True(Allowed(mouse, 0x00, 0x12, 0x07, 0x00, 0x00));
        Assert.False(Allowed(mouse, 0x03, 0x12, 0x07, 0x00, 0x00));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x07)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x13)]
    public void Cetra_reads_are_allowed(byte key)
    {
        Assert.True(Allowed(Cetra, 0xCC, 0x12, key, 0x00, 0x00));
    }

    [Theory]
    [InlineData(0xA2, 0x00)] // pairing mode
    [InlineData(0xA2, 0x01)] // unpair
    [InlineData(0xB0, 0x00)] // dongle reset
    public void Receiver_writes_are_refused(byte command, byte sub)
    {
        Assert.False(Allowed(OmniReceiver, 0x01, command, sub, 0x00, 0x00));
    }

    [Theory]
    [InlineData(0x50, 0x00)]
    [InlineData(0x50, 0x01)]
    [InlineData(0x51, 0x00)]
    [InlineData(0x51, 0x01)]
    [InlineData(0x51, 0x07)]
    public void Set_commands_are_refused(byte command, byte sub)
    {
        Assert.False(Allowed(OmniKeyboardSlot, 0x02, command, sub, 0x00, 0x00));
        Assert.False(Allowed(OmniMouseSlot, 0x03, command, sub, 0x00, 0x00));
    }

    [Fact]
    public void Non_zero_payload_is_refused()
    {
        var first = Frame(OmniKeyboardSlot, 0x02, 0x12, 0x01, 0x00, 0x00, 0x01);
        var last = Frame(OmniKeyboardSlot, 0x02, 0x12, 0x01, 0x00, 0x00);
        last[^1] = 0x01;

        Assert.False(_policy.IsAllowed(OmniKeyboardSlot, first));
        Assert.False(_policy.IsAllowed(OmniKeyboardSlot, last));
    }

    [Theory]
    [InlineData(0x12, 0x00, 0x01, 0x00)]
    [InlineData(0x12, 0x00, 0x0E, 0x00)] // wireless signal
    [InlineData(0x12, 0x00, 0x02, 0x01)]
    [InlineData(0x12, 0x01, 0x01, 0x00)]
    [InlineData(0x12, 0x02, 0x00, 0x00)]
    [InlineData(0x12, 0x05, 0x00, 0x00)]
    [InlineData(0x12, 0x08, 0x00, 0x00)]
    [InlineData(0x13, 0x01, 0x00, 0x00)]
    public void Other_device_commands_are_refused(byte command, byte sub, byte indexLo, byte indexHi)
    {
        Assert.False(Allowed(OmniKeyboardSlot, 0x02, command, sub, indexLo, indexHi));
    }

    [Fact]
    public void Receiver_commands_are_refused_on_slot_collections_and_device_commands_on_the_receiver()
    {
        Assert.False(Allowed(OmniKeyboardSlot, 0x02, 0xA0, 0x00, 0x00, 0x00));
        Assert.False(Allowed(OmniMouseSlot, 0x03, 0xA1, 0x00, 0x00, 0x00));
        Assert.False(Allowed(OmniReceiver, 0x01, 0x12, 0x01, 0x00, 0x00));
    }

    [Fact]
    public void Receiver_commands_are_refused_on_a_device_that_is_not_a_receiver()
    {
        Assert.False(Allowed(Collection(0x1C24, 0xFF02, 0x01), 0x01, 0xA0, 0x00, 0x00, 0x00));
    }

    [Fact]
    public void Report_id_must_be_the_collections_own()
    {
        Assert.False(Allowed(OmniKeyboardSlot, 0x03, 0x12, 0x01, 0x00, 0x00));
        Assert.False(Allowed(OmniReceiver, 0x02, 0xA0, 0x00, 0x00, 0x00));
    }

    [Fact]
    public void Short_frame_is_refused()
    {
        Assert.False(_policy.IsAllowed(OmniKeyboardSlot, new byte[] { 0x02, 0x12, 0x01, 0x00 }));
        Assert.False(_policy.IsAllowed(OmniKeyboardSlot, []));
    }

    [Fact]
    public void Other_vendors_and_unknown_asus_devices_are_refused()
    {
        Assert.False(Allowed(Collection(0x1ACE, 0xFF00, 0x02, vid: 0x046D), 0x02, 0x12, 0x01, 0x00, 0x00));
        // The ROG Gaming Display Aura controller on this PC (FF72, report EC, 65 bytes).
        Assert.False(Allowed(Collection(0x1BCA, 0xFF72, 0xEC, output: 65), 0xEC, 0x12, 0x01, 0x00, 0x00));
    }

    [Theory]
    [InlineData(0xFFC0)]
    [InlineData(0xFFC1)]
    [InlineData(0xFFC2)]
    [InlineData(0xFF03)]
    [InlineData(0xFF04)]
    [InlineData(0x0001)]
    public void Other_usage_pages_are_refused(int usagePage)
    {
        var collection = Collection(0x1ACE, (ushort)usagePage, 0x02);

        Assert.False(Allowed(collection, 0x02, 0x12, 0x01, 0x00, 0x00));
    }

    [Fact]
    public void Usage_other_than_1_is_refused()
    {
        Assert.False(Allowed(Collection(0x1ACE, 0xFF00, 0x02, usage: 2), 0x02, 0x12, 0x01, 0x00, 0x00));
    }

    [Theory]
    [InlineData(0x51, 0x12)]
    [InlineData(0x12, 0x14)]
    [InlineData(0x12, 0x02)]
    [InlineData(0x22, 0x12)]
    public void Cetra_writes_and_other_keys_are_refused(byte command, byte key)
    {
        Assert.False(Allowed(Cetra, 0xCC, command, key, 0x00, 0x00));
    }

    [Fact]
    public void Cetra_keys_need_report_CC_and_a_zero_index()
    {
        Assert.False(Allowed(Collection(0x1CAC, 0xFF00, 0x02), 0x02, 0x12, 0x12, 0x00, 0x00));
        Assert.False(Allowed(Cetra, 0xCC, 0x12, 0x12, 0x01, 0x00));
    }

    [Fact]
    public void Cetra_report_is_refused_on_other_devices()
    {
        Assert.False(Allowed(Collection(0x1ACE, 0xFF00, 0xCC), 0xCC, 0x12, 0x12, 0x00, 0x00));
    }
}
