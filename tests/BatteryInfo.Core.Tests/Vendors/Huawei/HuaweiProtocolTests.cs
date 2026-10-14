using BatteryInfo.Core.Vendors.Huawei;

namespace BatteryInfo.Core.Tests.Vendors.Huawei;

public class HuaweiProtocolTests
{
    // The battery reply captured from the FreeClip 2 (docs/protocols/huawei.md §7): L 76, R 75, case 100, not charging.
    internal const string BatteryReply = "5A 00 1B 00 01 08 01 01 4B 02 03 4C 4B 64 03 03 00 00 00 04 02 14 0A 05 02 00 00 06 01 0A AB 24";

    internal static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));

    [Theory]
    [InlineData("5A0009000108010002000300", 0xFBB9)]
    [InlineData("5A0009000108010140030100", 0xED2E)]
    [InlineData("5A0014000108010140020310203003030001000402140A", 0x1461)]
    [InlineData("5A00050001080100", 0xF53D)]
    public void Crc16_xmodem_matches_the_captured_vectors(string data, int crc) =>
        Assert.Equal((ushort)crc, HuaweiProtocol.Crc16Xmodem(Bytes(data)));

    [Fact]
    public void Battery_request_is_exactly_the_captured_frame() =>
        Assert.Equal(Bytes("5A 00 09 00 01 08 01 00 02 00 03 00 FB B9"), HuaweiProtocol.BuildBatteryRequest());

    [Fact]
    public void Build_frame_with_only_tlv_1_matches_the_short_request() =>
        Assert.Equal(Bytes("5A 00 05 00 01 08 01 00 F5 3D"), HuaweiProtocol.BuildFrame(0x01, 0x08, [0x01, 0x00]));

    [Fact]
    public void Build_frame_reproduces_the_captured_reply()
    {
        var reply = Bytes(BatteryReply);

        Assert.Equal(reply, HuaweiProtocol.BuildFrame(0x01, 0x08, reply.AsSpan(6, reply.Length - 8)));
    }
}
