using BatteryInfo.Core.Model;
using BatteryInfo.Core.Vendors.Razer;

namespace BatteryInfo.Core.Tests.Vendors.Razer;

public class RazerProtocolTests
{
    // 91-byte Windows buffer: [0] report id 0, then the 90-byte Razer report (razer.md §3, §4).
    private static byte[] Expected(byte size, byte commandClass, byte commandId, byte crc)
    {
        var buffer = new byte[91];
        buffer[2] = 0x1F;
        buffer[6] = size;
        buffer[7] = commandClass;
        buffer[8] = commandId;
        buffer[89] = crc;
        return buffer;
    }

    [Fact]
    public void Battery_level_request_matches_the_spec_bytes()
    {
        // 00 | 00 1F 00 00 00 02 07 80 | 80 x 00 | 85 00
        Assert.Equal(Expected(0x02, 0x07, 0x80, crc: 0x85), RazerProtocol.BatteryLevelRequest());
    }

    [Fact]
    public void Charging_status_request_matches_the_spec_bytes()
    {
        // 00 | 00 1F 00 00 00 02 07 84 | 80 x 00 | 81 00
        Assert.Equal(Expected(0x02, 0x07, 0x84, crc: 0x81), RazerProtocol.ChargingStatusRequest());
    }

    [Fact]
    public void Wireless_status_request_matches_the_spec_bytes()
    {
        // 00 | 00 1F 00 00 00 50 00 BF | 80 x 00 | EF 00
        Assert.Equal(Expected(0x50, 0x00, 0xBF, crc: 0xEF), RazerProtocol.WirelessStatusRequest());
    }

    [Fact]
    public void Requests_are_91_bytes_with_report_id_0_and_a_zero_reserved_byte()
    {
        var request = RazerProtocol.BatteryLevelRequest();

        Assert.Equal(RazerProtocol.BufferLength, request.Length);
        Assert.Equal(91, request.Length);
        Assert.Equal(0x00, request[0]);
        Assert.Equal(0x00, request[90]);
    }

    [Fact]
    public void Build_uses_the_given_transaction_id_and_arguments()
    {
        var request = RazerProtocol.Build(0x07, 0x80, 0x02, [0x00], transactionId: 0x3F);

        Assert.Equal(0x3F, request[2]);
        Assert.Equal(0x85, request[89]); // the txid is outside the CRC
    }

    [Fact]
    public void Crc_is_the_xor_of_report_bytes_2_to_87_and_skips_status_and_txid()
    {
        var buffer = new byte[91];
        buffer[1] = 0x02; // status: excluded
        buffer[2] = 0x1F; // txid: excluded
        buffer[6] = 0x02;
        buffer[7] = 0x07;
        buffer[8] = 0x80;
        buffer[10] = 0xF5;
        buffer[88] = 0x01; // last byte covered (report offset 87)
        buffer[90] = 0xAA; // reserved: excluded

        Assert.Equal(0x02 ^ 0x07 ^ 0x80 ^ 0xF5 ^ 0x01, RazerProtocol.Crc(buffer));
    }

    [Fact]
    public void Has_valid_crc_checks_byte_89()
    {
        var request = RazerProtocol.BatteryLevelRequest();
        Assert.True(RazerProtocol.HasValidCrc(request));

        request[89] ^= 0x01;
        Assert.False(RazerProtocol.HasValidCrc(request));
    }

    [Fact]
    public void Build_rejects_more_than_80_argument_bytes()
    {
        Assert.Throws<ArgumentException>(() => RazerProtocol.Build(0x07, 0x80, 0x02, new byte[81]));
    }

    // A reply as the device would hand it back: the request header echoed with a new status and arguments.
    internal static byte[] Reply(byte status, byte commandClass, byte commandId, byte size, byte[] args,
        byte transactionId = 0x1F, bool badCrc = false)
    {
        var buffer = new byte[91];
        buffer[1] = status;
        buffer[2] = transactionId;
        buffer[6] = size;
        buffer[7] = commandClass;
        buffer[8] = commandId;
        args.CopyTo(buffer, 9);
        buffer[89] = RazerProtocol.Crc(buffer);
        if (badCrc) buffer[89] ^= 0xFF;
        return buffer;
    }

    [Fact]
    public void Battery_reply_with_status_02_and_valid_crc_is_a_success()
    {
        // 02 1F 00 00 00 02 07 80 | 00 F5 | ... | CRC 70 = 02^07^80^00^F5
        var reply = new byte[91];
        byte[] header = [0x00, 0x02, 0x1F, 0x00, 0x00, 0x00, 0x02, 0x07, 0x80, 0x00, 0xF5];
        header.CopyTo(reply, 0);
        reply[89] = 0x70;

        Assert.Equal(RazerReplyKind.Success, RazerProtocol.Classify(RazerProtocol.BatteryLevelRequest(), reply));
    }

    [Theory]
    [InlineData(0x00, RazerReplyKind.Pending)]
    [InlineData(0x01, RazerReplyKind.Pending)]
    [InlineData(0x02, RazerReplyKind.Success)]
    [InlineData(0x03, RazerReplyKind.Failure)]
    [InlineData(0x04, RazerReplyKind.NoResponse)]
    [InlineData(0x05, RazerReplyKind.NotSupported)]
    [InlineData(0x06, RazerReplyKind.Pending)]
    public void Status_byte_decides_the_kind_of_a_matching_reply(byte status, RazerReplyKind expected)
    {
        var reply = Reply(status, 0x07, 0x80, 0x02, [0x00, 0xF5]);

        Assert.Equal(expected, RazerProtocol.Classify(RazerProtocol.BatteryLevelRequest(), reply));
    }

    [Fact]
    public void Success_with_a_bad_crc_is_flagged()
    {
        var reply = Reply(0x02, 0x07, 0x80, 0x02, [0x00, 0xF5], badCrc: true);

        Assert.Equal(RazerReplyKind.BadCrc, RazerProtocol.Classify(RazerProtocol.BatteryLevelRequest(), reply));
    }

    [Fact]
    public void The_untouched_request_echo_is_pending()
    {
        var request = RazerProtocol.BatteryLevelRequest();

        Assert.Equal(RazerReplyKind.Pending, RazerProtocol.Classify(request, request.ToArray()));
    }

    public static TheoryData<string, byte[]> ForeignReplies => new()
    {
        // Synapse clients share the MI_00 mailbox and also use txid 0x1F (razer.md §5).
        { "same txid, other class", Reply(0x02, 0x0F, 0x80, 0x02, [0x00, 0xF5]) },
        { "same txid and class, other id", Reply(0x02, 0x07, 0x84, 0x02, [0x00, 0x01]) },
        { "same txid, class and id, other size", Reply(0x02, 0x07, 0x80, 0x03, [0x00, 0xF5]) },
        { "same header, other battery id", Reply(0x02, 0x07, 0x80, 0x02, [0x01, 0xF5]) },
        { "other txid", Reply(0x02, 0x07, 0x80, 0x02, [0x00, 0xF5], transactionId: 0x05) },
        { "other txid, status 04", Reply(0x04, 0x07, 0x80, 0x02, [0x00, 0x00], transactionId: 0x05) },
        { "busy for another command", Reply(0x01, 0x0F, 0x02, 0x0C, []) },
    };

    [Theory]
    [MemberData(nameof(ForeignReplies))]
    public void Replies_to_other_commands_are_foreign(string _, byte[] reply)
    {
        Assert.Equal(RazerReplyKind.Foreign, RazerProtocol.Classify(RazerProtocol.BatteryLevelRequest(), reply));
    }

    [Fact]
    public void A_buffer_with_another_report_id_or_length_is_foreign()
    {
        var request = RazerProtocol.BatteryLevelRequest();
        var reply = Reply(0x02, 0x07, 0x80, 0x02, [0x00, 0xF5]);
        var otherId = reply.ToArray();
        otherId[0] = 0x05;

        Assert.Equal(RazerReplyKind.Foreign, RazerProtocol.Classify(request, otherId));
        Assert.Equal(RazerReplyKind.Foreign, RazerProtocol.Classify(request, reply.AsSpan(0, 90)));
    }

    [Theory]
    [InlineData(245, 96)]
    [InlineData(255, 100)]
    [InlineData(217, 85)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    // Observed pairs (razer.md §4a).
    [InlineData(220, 86)]
    [InlineData(222, 87)]
    [InlineData(225, 88)]
    [InlineData(227, 89)]
    [InlineData(230, 90)]
    [InlineData(233, 91)]
    [InlineData(235, 92)]
    [InlineData(238, 93)]
    [InlineData(240, 94)]
    [InlineData(243, 95)]
    [InlineData(248, 97)]
    [InlineData(250, 98)]
    [InlineData(253, 99)]
    public void Battery_percent_is_raw_times_100_over_255_rounded_down(byte raw, int percent)
    {
        Assert.Equal(percent, RazerProtocol.BatteryPercent(raw));
        Assert.Equal(percent, RazerProtocol.ParseBatteryPercent(Reply(0x02, 0x07, 0x80, 0x02, [0x00, raw])));
    }

    [Theory]
    [InlineData(0, ChargeState.Discharging)] // NoCharge_BatteryFull: on battery
    [InlineData(1, ChargeState.Charging)]
    [InlineData(2, ChargeState.Fault)] // ChargingFailed
    [InlineData(3, ChargeState.Fault)] // ForeignObjectDetection
    [InlineData(4, ChargeState.Charging)] // FastCharging
    [InlineData(5, ChargeState.NotCharging)] // FullCharging: on power, held
    [InlineData(6, ChargeState.NotCharging)] // ReachChargingLimit
    [InlineData(7, ChargeState.Unknown)]
    [InlineData(0xFF, ChargeState.Unknown)]
    public void Charging_status_maps_the_synapse_enum(byte value, ChargeState expected)
    {
        Assert.Equal(expected, RazerProtocol.ChargeStateFrom(value));
        Assert.Equal(expected, RazerProtocol.ParseChargeState(Reply(0x02, 0x07, 0x84, 0x02, [0x00, value])));
    }

    [Fact]
    public void Wireless_status_lists_linked_and_empty_slots()
    {
        // args 02 | 01 00 D7 | 00 FF FF: two slots, slot 1 linked to PID 00D7, slot 2 empty.
        var slots = RazerProtocol.ParseWirelessStatus(Reply(0x02, 0x00, 0xBF, 0x50, [0x02, 0x01, 0x00, 0xD7, 0x00, 0xFF, 0xFF]));

        Assert.Equal([new RazerLinkSlot(true, 0x00D7), new RazerLinkSlot(false, 0xFFFF)], slots);
        Assert.False(slots[0].IsEmpty);
        Assert.True(slots[1].IsEmpty);
    }

    [Fact]
    public void Wireless_status_reports_an_unlinked_mouse()
    {
        // args 02 | 00 00 D7 | 00 FF FF: the mouse is off or out of range.
        var slots = RazerProtocol.ParseWirelessStatus(Reply(0x02, 0x00, 0xBF, 0x50, [0x02, 0x00, 0x00, 0xD7, 0x00, 0xFF, 0xFF]));

        Assert.Equal(new RazerLinkSlot(false, 0x00D7), slots[0]);
    }

    [Fact]
    public void Wireless_status_count_is_capped_to_the_argument_area()
    {
        var args = new byte[80];
        args[0] = 0xFF;

        Assert.Equal(26, RazerProtocol.ParseWirelessStatus(Reply(0x02, 0x00, 0xBF, 0x50, args)).Count);
    }

    [Fact]
    public void Wireless_status_reply_matches_whatever_its_first_argument()
    {
        // For 00/BF args[0] is the slot count, not an echoed id.
        var reply = Reply(0x02, 0x00, 0xBF, 0x50, [0x02, 0x01, 0x00, 0xD7, 0x00, 0xFF, 0xFF]);

        Assert.Equal(RazerReplyKind.Success, RazerProtocol.Classify(RazerProtocol.WirelessStatusRequest(), reply));
    }
}
