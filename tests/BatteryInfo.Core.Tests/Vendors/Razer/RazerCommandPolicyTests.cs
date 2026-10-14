using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Razer;

namespace BatteryInfo.Core.Tests.Vendors.Razer;

public class RazerCommandPolicyTests
{
    private readonly RazerCommandPolicy _policy = new();
    private readonly FakeHidBackend _backend = new();

    private HidDeviceInfo Device(ushort pid, ushort vid = 0x1532) =>
        _backend.Add(vid, pid, 0x0001, 0x0002, 8, 0, feature: 91, mi: 0).Info;

    private HidDeviceInfo Wired => Device(0x00D6);

    private HidDeviceInfo Dongle => Device(0x00D7);

    private static byte[] Get(byte commandClass, byte commandId, byte size, params byte[] args) =>
        RazerProtocol.Build(commandClass, commandId, size, args);

    // Re-seals the CRC after a test edits a frame, so the edit itself is what the policy judges.
    private static byte[] Sealed(byte[] frame)
    {
        frame[89] = RazerProtocol.Crc(frame);
        return frame;
    }

    [Fact]
    public void Battery_and_charging_reads_are_allowed_on_cable_and_dongle()
    {
        foreach (var device in new[] { Wired, Dongle })
        {
            Assert.True(_policy.IsAllowed(device, RazerProtocol.BatteryLevelRequest()));
            Assert.True(_policy.IsAllowed(device, RazerProtocol.ChargingStatusRequest()));
        }
    }

    [Fact]
    public void Wireless_status_is_allowed_on_the_dongle_only()
    {
        Assert.True(_policy.IsAllowed(Dongle, RazerProtocol.WirelessStatusRequest()));
        Assert.False(_policy.IsAllowed(Wired, RazerProtocol.WirelessStatusRequest()));
    }

    public static TheoryData<byte> Classes => new() { 0x00, 0x02, 0x03, 0x04, 0x05, 0x07, 0x0D, 0x0F };

    [Theory]
    [MemberData(nameof(Classes))]
    public void Every_set_command_is_refused(byte commandClass)
    {
        for (var id = 0x00; id < 0x80; id++)
        {
            foreach (var size in new byte[] { 0x00, 0x01, 0x02, 0x03, 0x50 })
            {
                Assert.False(_policy.IsAllowed(Dongle, Get(commandClass, (byte)id, size)),
                    $"SET {commandClass:X2}/{id:X2} size {size:X2} must be refused");
            }
        }
    }

    public static TheoryData<string, byte[]> DangerousNeighbours => new()
    {
        { "set device mode (driver mode)", Get(0x00, 0x04, 0x02, 0x03, 0x00) },
        { "set polling rate", Get(0x00, 0x05, 0x01, 0x01) },
        { "set low battery threshold", Get(0x07, 0x01, 0x01, 0x26) },
        { "set idle time", Get(0x07, 0x03, 0x02, 0x01, 0x2C) },
        { "set DPI", Get(0x04, 0x05, 0x07, 0x00, 0x03, 0x20, 0x03, 0x20) },
        { "set matrix effect", Get(0x0F, 0x02, 0x06, 0x01, 0x00, 0x00) },
        { "get edition: a read, but not on the list", Get(0x00, 0x86, 0x03) },
        { "get firmware: a read, but not on the list", Get(0x00, 0x81, 0x02) },
        { "get idle time: a read, but not on the list", Get(0x07, 0x83, 0x02) },
        { "battery with battery id 1", Get(0x07, 0x80, 0x02, 0x01) },
        { "battery with a stray argument", Get(0x07, 0x80, 0x02, 0x00, 0x00, 0x00, 0x00, 0x01) },
        { "battery with size 0x50", Get(0x07, 0x80, 0x50) },
        { "charging with size 0x01", Get(0x07, 0x84, 0x01) },
        { "wireless status with size 0x02", Get(0x00, 0xBF, 0x02) },
        { "wireless status with arguments", Get(0x00, 0xBF, 0x50, 0x01) },
    };

    [Theory]
    [MemberData(nameof(DangerousNeighbours))]
    public void Dangerous_neighbours_are_refused(string what, byte[] frame)
    {
        Assert.False(_policy.IsAllowed(Dongle, frame), what);
        Assert.False(_policy.IsAllowed(Wired, frame), what);
    }

    [Fact]
    public void A_frame_with_a_bad_crc_is_refused()
    {
        var frame = RazerProtocol.BatteryLevelRequest();
        frame[89] ^= 0x01;

        Assert.False(_policy.IsAllowed(Dongle, frame));
    }

    [Theory]
    [InlineData(0, 0x01)] // report id
    [InlineData(1, 0x02)] // status: hosts send 0x00
    [InlineData(3, 0x01)] // remaining packets
    [InlineData(4, 0x01)]
    [InlineData(5, 0x01)] // protocol type
    [InlineData(90, 0x01)] // reserved
    public void A_frame_with_a_non_zero_header_or_trailer_byte_is_refused(int index, byte value)
    {
        var frame = RazerProtocol.BatteryLevelRequest();
        frame[index] = value;

        Assert.False(_policy.IsAllowed(Dongle, Sealed(frame)));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(64)]
    [InlineData(92)]
    [InlineData(0)]
    public void A_frame_of_the_wrong_length_is_refused(int length)
    {
        var frame = new byte[length];
        RazerProtocol.BatteryLevelRequest().AsSpan(0, Math.Min(length, 91)).CopyTo(frame);

        Assert.False(_policy.IsAllowed(Dongle, frame));
    }

    [Theory]
    [InlineData(0x1532, 0x0099)] // a Razer product not in the model table
    [InlineData(0x1532, 0x00D8)] // Bluetooth PID
    [InlineData(0x068E, 0x00D8)] // BLE vendor id
    [InlineData(0x046D, 0x00D7)] // another vendor with a colliding PID
    public void Allowed_frames_are_refused_on_other_devices(ushort vid, ushort pid)
    {
        var device = Device(pid, vid);

        Assert.False(_policy.IsAllowed(device, RazerProtocol.BatteryLevelRequest()));
        Assert.False(_policy.IsAllowed(device, RazerProtocol.ChargingStatusRequest()));
        Assert.False(_policy.IsAllowed(device, RazerProtocol.WirelessStatusRequest()));
    }
}
