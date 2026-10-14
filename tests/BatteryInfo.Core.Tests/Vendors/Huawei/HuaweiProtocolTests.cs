using BatteryInfo.Core.Model;
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

    private static IReadOnlyList<BatteryComponent>? Parse(string payloadHex) => HuaweiProtocol.ParseBattery(Bytes(payloadHex));

    private static byte[] PayloadOf(string frameHex)
    {
        var frame = Bytes(frameHex);
        return frame[6..^2];
    }

    [Fact]
    public void Captured_reply_gives_left_right_and_case()
    {
        var components = HuaweiProtocol.ParseBattery(PayloadOf(BatteryReply));

        Assert.Equal(
            [
                new BatteryComponent("Left", 76, ChargeState.Discharging),
                new BatteryComponent("Right", 75, ChargeState.Discharging),
                new BatteryComponent("Case", 100, ChargeState.Discharging),
            ],
            components);
    }

    [Fact]
    public void Single_battery_fixture_gives_one_component_from_tlv_1()
    {
        // OpenFreebuds legacy fixture 5A0009000108010140030100ED2E: T1 = 64 %, one-byte T3 = 0.
        var components = HuaweiProtocol.ParseBattery(PayloadOf("5A0009000108010140030100ED2E"));

        Assert.Equal([BatteryComponent.Single(64, ChargeState.Discharging)], components);
    }

    [Fact]
    public void Charging_flags_map_per_index_to_left_right_case()
    {
        // CRC vector frame 5A0014...0A + 1461: T2 = 16/32/48, T3 = 00 01 00.
        var components = HuaweiProtocol.ParseBattery(PayloadOf("5A0014000108010140020310203003030001000402140A1461"));

        Assert.Equal(
            [
                new BatteryComponent("Left", 16, ChargeState.Discharging),
                new BatteryComponent("Right", 32, ChargeState.Charging),
                new BatteryComponent("Case", 48, ChargeState.Discharging),
            ],
            components);
    }

    [Fact]
    public void Missing_tlv_2_falls_back_to_tlv_1() =>
        Assert.Equal([BatteryComponent.Single(75, ChargeState.Discharging)], Parse("01 01 4B 03 03 00 00 00"));

    [Fact]
    public void Tlv_2_that_is_not_three_bytes_falls_back_to_tlv_1() =>
        Assert.Equal([BatteryComponent.Single(75, ChargeState.Charging)], Parse("01 01 4B 02 02 4C 4B 03 03 00 01 00"));

    [Fact]
    public void Values_above_100_are_unknown()
    {
        var components = Parse("01 01 4B 02 03 4C FF 64 03 03 00 00 00");

        Assert.Equal(
            [
                new BatteryComponent("Left", 76, ChargeState.Discharging),
                new BatteryComponent("Right", null, ChargeState.Discharging),
                new BatteryComponent("Case", 100, ChargeState.Discharging),
            ],
            components);
    }

    [Fact]
    public void Tlv_2_without_any_valid_value_falls_back_to_tlv_1() =>
        Assert.Equal([BatteryComponent.Single(75, ChargeState.Unknown)], Parse("01 01 4B 02 03 FF FF FF"));

    [Fact]
    public void Tlv_1_above_100_without_tlv_2_gives_nothing() => Assert.Null(Parse("01 01 FF"));

    [Fact]
    public void Missing_tlv_3_leaves_the_charge_state_unknown() =>
        Assert.Equal(
            [
                new BatteryComponent("Left", 76, ChargeState.Unknown),
                new BatteryComponent("Right", 75, ChargeState.Unknown),
                new BatteryComponent("Case", 100, ChargeState.Unknown),
            ],
            Parse("02 03 4C 4B 64"));

    [Fact]
    public void Tlv_order_does_not_matter() =>
        Assert.Equal(
            Parse("01 01 4B 02 03 4C 4B 64 03 03 01 00 00"),
            Parse("03 03 01 00 00 02 03 4C 4B 64 01 01 4B"));

    [Fact]
    public void Unknown_tlvs_are_ignored_including_varint_lengths()
    {
        // Type 0x09 with a two-byte VarInt length 81 00 = 128 bytes of value, then T2.
        var payload = "09 81 00 " + string.Concat(Enumerable.Repeat("AA ", 128)) + "02 03 4C 4B 64";

        Assert.Equal(76, Parse(payload)![0].Percent);
    }

    [Fact]
    public void Truncated_tlv_gives_nothing() => Assert.Null(Parse("01 01 4B 02 03 4C 4B"));

    [Fact]
    public void Reply_without_battery_values_gives_nothing() => Assert.Null(Parse("04 02 14 0A 05 02 00 00"));

    [Fact]
    public void Error_result_tlv_gives_nothing() => Assert.Null(Parse("7F 04 00 01 86 A1 02 03 4C 4B 64"));

    [Fact]
    public void Success_result_tlv_is_accepted() => Assert.Equal(3, Parse("7F 04 00 01 86 A0 02 03 4C 4B 64")!.Count);

    [Theory]
    [InlineData(0x01, 0x08, true)]
    [InlineData(0x01, 0x27, true)]
    [InlineData(0x01, 0x06, false)]
    [InlineData(0x2B, 0x08, false)]
    public void Battery_frames_are_service_1_commands_8_and_27(byte service, byte command, bool expected) =>
        Assert.Equal(expected, HuaweiProtocol.IsBatteryFrame(new HuaweiFrame(service, command, [], [])));
}
