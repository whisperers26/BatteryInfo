using BatteryInfo.Core.Vendors.Logitech;

namespace BatteryInfo.Core.Tests.Vendors.Logitech;

// Frame vectors from docs/protocols/logitech.md. The captured replies were taken with swId 0x0E.
public class HidppFrameTests
{
    private static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static byte[] Long(string hex)
    {
        var report = new byte[HidppProtocol.LongLength];
        Bytes(hex).CopyTo(report, 0);
        return report;
    }

    [Fact]
    public void Ping_is_iroot_get_protocol_version_with_the_mark_in_the_last_byte()
    {
        Assert.Equal(Bytes("10 01 00 1C 00 00 A2"), HidppProtocol.Ping(0x01, 0x0C, 0xA2));
    }

    [Fact]
    public void Get_feature_puts_the_feature_id_big_endian_in_the_params()
    {
        Assert.Equal(Bytes("10 01 00 0C 10 04 00"), HidppProtocol.GetFeature(0x01, 0x1004, 0x0C));
    }

    [Fact]
    public void Feature_request_puts_function_and_software_id_in_byte_3()
    {
        Assert.Equal(Bytes("10 01 06 1C 00 00 00"), HidppProtocol.FeatureRequest(0x01, 0x06, 1, 0x0C));
        Assert.Equal(Bytes("10 01 03 1C 10 00 00"), HidppProtocol.FeatureRequest(0x01, 0x03, 1, 0x0C, 0x10));
    }

    [Fact]
    public void Register_reads_are_short_get_register_frames()
    {
        Assert.Equal(Bytes("10 FF 83 B5 03 00 00"), HidppProtocol.ReadReceiverInfo(0x03));
        Assert.Equal(Bytes("10 FF 81 02 00 00 00"), HidppProtocol.ReadRegister(0xFF, 0x02));
        Assert.Equal(Bytes("10 02 81 0D 00 00 00"), HidppProtocol.ReadRegister(0x02, 0x0D));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void Software_id_must_be_1_to_15(int swId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HidppProtocol.Ping(0x01, (byte)swId, 0x01));
    }

    [Fact]
    public void Function_must_fit_in_a_nibble()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HidppProtocol.FeatureRequest(0x01, 0x06, 16, 0x0C));
    }

    [Fact]
    public void Ping_reply_matches_and_gives_the_protocol_version()
    {
        var request = HidppProtocol.Ping(0x01, 0x0E, 0xA2);
        var reply = Long("11 01 00 1E 04 02 A2");

        Assert.Equal(new HidppMatch(HidppMatchKind.Reply), HidppProtocol.Match(request, reply));
        Assert.Equal((4, 2), HidppProtocol.ParsePingReply(reply));
    }

    [Fact]
    public void Ping_reply_with_another_mark_is_not_ours()
    {
        var request = HidppProtocol.Ping(0x01, 0x0E, 0xA3);

        Assert.Equal(HidppMatchKind.None, HidppProtocol.Match(request, Long("11 01 00 1E 04 02 A2")).Kind);
    }

    [Fact]
    public void Reply_with_another_software_id_belongs_to_other_software()
    {
        var request = HidppProtocol.FeatureRequest(0x01, 0x06, 1, 0x0C);

        Assert.Equal(HidppMatchKind.None, HidppProtocol.Match(request, Long("11 01 06 1B 38 04 00 00")).Kind);
        Assert.Equal(HidppMatchKind.None, HidppProtocol.Match(request, Long("11 01 06 1D 38 04 00 00")).Kind);
        Assert.Equal(HidppMatchKind.Reply, HidppProtocol.Match(request, Long("11 01 06 1C 38 04 00 00")).Kind);
    }

    [Fact]
    public void Reply_for_another_device_or_function_is_not_ours()
    {
        var request = HidppProtocol.FeatureRequest(0x01, 0x06, 1, 0x0C);

        Assert.Equal(HidppMatchKind.None, HidppProtocol.Match(request, Long("11 02 06 1C 38 04 00 00")).Kind);
        Assert.Equal(HidppMatchKind.None, HidppProtocol.Match(request, Long("11 01 06 0C 0F 0F 02 00")).Kind);
    }

    [Fact]
    public void Ping_error_from_the_receiver_is_a_hidpp10_error()
    {
        var request = HidppProtocol.Ping(0x01, 0x0E, 0x55);

        Assert.Equal(new HidppMatch(HidppMatchKind.Hidpp10Error, 0x09), HidppProtocol.Match(request, Bytes("10 01 8F 00 1E 09 00")));
    }

    [Fact]
    public void Empty_slot_answers_a_long_register_read_with_a_hidpp10_error()
    {
        var request = HidppProtocol.ReadReceiverInfo(0x21);

        Assert.Equal(new HidppMatch(HidppMatchKind.Hidpp10Error, 0x09), HidppProtocol.Match(request, Bytes("10 FF 8F 83 B5 09 00")));
    }

    [Fact]
    public void Long_register_reply_must_echo_the_sub_register()
    {
        var request = HidppProtocol.ReadReceiverInfo(0x20);

        Assert.Equal(HidppMatchKind.None, HidppProtocol.Match(request, Long("11 FF 83 B5 30 B4 65 58 32")).Kind);
        Assert.Equal(HidppMatchKind.Reply, HidppProtocol.Match(request, Long("11 FF 83 B5 20 08 08 40 BD 11 00 02 B1")).Kind);
    }

    [Fact]
    public void Short_register_reply_matches()
    {
        var request = HidppProtocol.ReadRegister(0xFF, 0x02);

        Assert.Equal(HidppMatchKind.Reply, HidppProtocol.Match(request, Bytes("10 FF 81 02 00 01 00")).Kind);
    }

    [Fact]
    public void Hidpp20_error_carries_the_error_code()
    {
        var request = HidppProtocol.FeatureRequest(0x01, 0x06, 1, 0x0C);

        Assert.Equal(new HidppMatch(HidppMatchKind.Hidpp20Error, 0x06), HidppProtocol.Match(request, Long("11 01 FF 06 1C 06 00")));
        Assert.Equal(HidppMatchKind.None, HidppProtocol.Match(request, Long("11 01 FF 06 1B 06 00")).Kind);
    }

    [Fact]
    public void Truncated_reports_never_match()
    {
        var request = HidppProtocol.FeatureRequest(0x01, 0x06, 1, 0x0C);

        Assert.Equal(HidppMatchKind.None, HidppProtocol.Match(request, Bytes("11 01 06 1C")).Kind);
    }

    [Fact]
    public void Get_feature_reply_gives_index_and_version()
    {
        var reply = Long("11 01 00 0E 06 00 05");

        Assert.Equal(new HidppFeatureInfo(0x06, 0x00, 0x05), HidppProtocol.ParseFeatureInfo(reply));
    }

    [Fact]
    public void Get_feature_id_asks_feature_set_fn1_for_the_index_in_p0()
    {
        Assert.Equal(Bytes("10 01 01 1C 06 00 00"), HidppProtocol.GetFeatureId(0x01, featureSetIndex: 0x01, featureIndex: 0x06, 0x0C));
    }

    [Fact]
    public void Get_feature_id_reply_gives_the_feature_id_big_endian()
    {
        // Verified: getFeatureId(06) -> 10 04 00 05 (feature 0x1004, type 0, version 5).
        var request = HidppProtocol.GetFeatureId(0x01, 0x01, 0x06, 0x0E);
        var reply = Long("11 01 01 1E 10 04 00 05");

        Assert.Equal(HidppMatchKind.Reply, HidppProtocol.Match(request, reply).Kind);
        Assert.Equal(HidppFeature.UnifiedBattery, HidppProtocol.ParseFeatureId(reply));
    }

    [Theory]
    [InlineData("10 01 41 0C 02 BD 40", true)]
    [InlineData("10 01 41 0C 42 BD 40", false)]
    public void Receiver_connection_notification_gives_the_link_state(string hex, bool linkEstablished)
    {
        Assert.True(HidppProtocol.TryParseConnectionNotification(Bytes(hex), out var deviceIndex, out var link));
        Assert.Equal(0x01, deviceIndex);
        Assert.Equal(linkEstablished, link);
    }

    [Fact]
    public void Feature_reply_is_not_a_connection_notification()
    {
        Assert.False(HidppProtocol.TryParseConnectionNotification(Long("11 01 06 1C 38 04 00 00"), out _, out _));
    }

    [Fact]
    public void Wireless_status_event_with_status_1_is_a_reconnect()
    {
        var report = Long("11 01 04 00 01 01 01");

        Assert.True(HidppProtocol.TryParseFeatureEvent(report, out var deviceIndex, out var featureIndex, out var eventNumber));
        Assert.Equal(0x01, deviceIndex);
        Assert.Equal(0x04, featureIndex);
        Assert.Equal(0, eventNumber);
        Assert.True(HidppProtocol.IsReconnectEvent(report, wirelessStatusIndex: 0x04));
        Assert.False(HidppProtocol.IsReconnectEvent(Long("11 01 04 00 00 00 00"), wirelessStatusIndex: 0x04));
        Assert.False(HidppProtocol.IsReconnectEvent(report, wirelessStatusIndex: 0x05));
    }

    [Theory]
    [InlineData("11 01 06 1C 38 04 00 00")] // a reply (software id set)
    [InlineData("10 01 41 0C 02 BD 40")]    // a receiver notification
    [InlineData("10 FF 8F 83 B5 09 00")]    // an error
    public void Replies_notifications_and_errors_are_not_feature_events(string hex)
    {
        Assert.False(HidppProtocol.TryParseFeatureEvent(Bytes(hex), out _, out _, out _));
    }
}
