using System.Text;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Vendors.Logitech;

namespace BatteryInfo.Core.Tests.Vendors.Logitech;

// Receiver register and device info vectors from docs/protocols/logitech.md §4 and §6.
public class HidppRegisterTests
{
    private static byte[] Long(string hex)
    {
        var report = new byte[HidppProtocol.LongLength];
        Convert.FromHexString(hex.Replace(" ", "")).CopyTo(report, 0);
        return report;
    }

    private static byte[] LongWithText(string headerHex, string text)
    {
        var report = Long(headerHex);
        var header = Convert.FromHexString(headerHex.Replace(" ", "")).Length;
        Encoding.ASCII.GetBytes(text).CopyTo(report, header);
        return report;
    }

    [Fact]
    public void Receiver_info_gives_serial_and_max_devices()
    {
        var info = HidppProtocol.ParseReceiverInfo(Long("11 FF 83 B5 03 A4 08 25 0B 01 02 08 FF 00"));

        Assert.Equal(new HidppReceiverInfo(0xA408250B, 2), info);
    }

    [Fact]
    public void Pairing_info_gives_wpid_and_kind()
    {
        var info = HidppProtocol.ParsePairingInfo(Long("11 FF 83 B5 20 08 08 40 BD 11 00 02 B1 01 00 00 7F 24 00 00"));

        Assert.Equal(new HidppPairingInfo(0x40BD, 2), info);
    }

    [Fact]
    public void Extended_pairing_gives_the_device_serial()
    {
        Assert.Equal(0xB4655832u, HidppProtocol.ParseExtendedPairingSerial(Long("11 FF 83 B5 30 B4 65 58 32 00 00 00 00 00 01")));
    }

    [Fact]
    public void Codename_is_length_prefixed_ascii()
    {
        Assert.Equal("X2 SUPERSTRIKE", HidppProtocol.ParseCodename(LongWithText("11 FF 83 B5 40 0E", "X2 SUPERSTRIKE")));
    }

    [Fact]
    public void Codename_length_is_clamped_to_the_report()
    {
        Assert.Equal("X2 SUPERSTRIKE", HidppProtocol.ParseCodename(LongWithText("11 FF 83 B5 40 7F", "X2 SUPERSTRIKE")));
    }

    [Fact]
    public void Device_info_gives_unit_id_and_model_ids_in_transport_bit_order()
    {
        var ids = HidppProtocol.ParseDeviceIds(Long("11 01 02 0C 02 B4 65 58 32 00 0C 40 BD C0 A8 00 00 01 01"));

        Assert.Equal(new HidppDeviceIds(0xB4655832, Wpid: 0x40BD, UsbPid: 0xC0A8), ids);
    }

    [Fact]
    public void Device_info_without_a_wireless_transport_has_no_wpid()
    {
        var ids = HidppProtocol.ParseDeviceIds(Long("11 FF 02 0C 01 B4 65 58 32 00 08 C0 A8 00 00 00 00 01 01"));

        Assert.Equal(new HidppDeviceIds(0xB4655832, Wpid: null, UsbPid: 0xC0A8), ids);
    }

    [Fact]
    public void Name_is_read_in_16_byte_chunks()
    {
        Assert.Equal(18, HidppProtocol.ParseNameLength(Long("11 01 03 0C 12")));

        var first = HidppProtocol.NameChunk(LongWithText("11 01 03 1C", "PRO X2 SUPERSTRI"), remaining: 18);
        var second = HidppProtocol.NameChunk(LongWithText("11 01 03 1C", "KE"), remaining: 2);

        Assert.Equal(16, first.Length);
        Assert.Equal(2, second.Length);
        Assert.Equal("PRO X2 SUPERSTRIKE", HidppProtocol.DecodeName([.. first, .. second]));
    }

    [Fact]
    public void Name_stops_at_a_nul()
    {
        Assert.Equal("G PRO", HidppProtocol.DecodeName("G PRO\0\0\0"u8));
    }

    [Theory]
    [InlineData(0, DeviceKind.Keyboard)]
    [InlineData(2, DeviceKind.Keyboard)]
    [InlineData(3, DeviceKind.Mouse)]
    [InlineData(4, DeviceKind.Mouse)]
    [InlineData(5, DeviceKind.Mouse)]
    [InlineData(1, DeviceKind.Other)]
    [InlineData(7, DeviceKind.Other)]
    public void Device_name_type_maps_to_a_kind(int type, DeviceKind kind)
    {
        Assert.Equal(kind, HidppProtocol.KindFromDeviceType((byte)type));
    }

    [Fact]
    public void Device_type_reply_gives_mouse()
    {
        Assert.Equal(DeviceKind.Mouse, HidppProtocol.KindFromDeviceType(HidppProtocol.ParseDeviceType(Long("11 01 03 2C 03"))));
    }

    [Theory]
    [InlineData(1, DeviceKind.Keyboard)]
    [InlineData(2, DeviceKind.Mouse)]
    [InlineData(0, DeviceKind.Other)]
    public void Pairing_kind_maps_to_a_kind(int kind, DeviceKind expected)
    {
        Assert.Equal(expected, HidppProtocol.KindFromPairingKind((byte)kind));
    }
}
