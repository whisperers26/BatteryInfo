using BatteryInfo.Core.Vendors.Huawei;
using static BatteryInfo.Core.Tests.Vendors.Huawei.HuaweiProtocolTests;

namespace BatteryInfo.Core.Tests.Vendors.Huawei;

public class HuaweiFrameReaderTests
{
    // Unsolicited frames the FreeClip 2 sends on every RFCOMM open, and an idle dual-connect event (§8).
    internal const string Preamble = "5A 00 03 00 01 06 3E BD";
    internal const string Hello = "5A 00 27 00 2B AC 01 01 01 02 01 01 03 01 00 04 01 00 05 01 00 06 01 00 07 01 00 08 01 00 09 01 00 0A 01 00 0B 01 00 0E 01 00 3F FE";
    internal const string DualConnect = "5A 00 0C 00 2B 36 05 07 60 34 DB 3C F2 AC 03 D9 A5";

    private readonly HuaweiFrameReader _reader = new();

    private List<HuaweiFrame> Drain()
    {
        var frames = new List<HuaweiFrame>();
        while (_reader.TryRead(out var frame)) frames.Add(frame);
        return frames;
    }

    private static (byte Service, byte Command) Ids(HuaweiFrame frame) => (frame.Service, frame.Command);

    [Fact]
    public void Splits_one_chunk_into_frames_in_order()
    {
        _reader.Append(Bytes(Preamble + BatteryReply + Hello + DualConnect));

        var frames = Drain();

        Assert.Equal([(0x01, 0x06), (0x01, 0x08), (0x2B, 0xAC), (0x2B, 0x36)], frames.Select(Ids));
        Assert.Equal(Bytes(BatteryReply), frames[1].Raw);
        Assert.Equal(Bytes("01 01 4B 02 03 4C 4B 64 03 03 00 00 00 04 02 14 0A 05 02 00 00 06 01 0A"), frames[1].Payload);
        Assert.Empty(frames[0].Payload);
    }

    [Fact]
    public void Reassembles_frames_fed_one_byte_at_a_time()
    {
        var frames = new List<HuaweiFrame>();
        foreach (var b in Bytes(Preamble + Preamble + BatteryReply + Hello))
        {
            _reader.Append([b]);
            while (_reader.TryRead(out var frame)) frames.Add(frame);
        }

        Assert.Equal([(0x01, 0x06), (0x01, 0x06), (0x01, 0x08), (0x2B, 0xAC)], frames.Select(Ids));
    }

    [Fact]
    public void Waits_for_the_rest_of_an_incomplete_frame()
    {
        var reply = Bytes(BatteryReply);
        _reader.Append(reply.AsSpan(0, 10));

        Assert.False(_reader.TryRead(out _));

        _reader.Append(reply.AsSpan(10));
        Assert.True(_reader.TryRead(out var frame));
        Assert.Equal(reply, frame.Raw);
    }

    [Fact]
    public void Skips_garbage_between_frames()
    {
        _reader.Append(Bytes("00 FF 13 " + Preamble + "77 A5 " + BatteryReply + "01 02"));

        Assert.Equal([(0x01, 0x06), (0x01, 0x08)], Drain().Select(Ids));
    }

    [Fact]
    public void Drops_a_frame_with_a_bad_crc_and_resyncs()
    {
        var corrupt = Bytes(BatteryReply);
        corrupt[^1] ^= 0x01;
        _reader.Append(corrupt);
        _reader.Append(Bytes(Preamble));

        Assert.Equal([(0x01, 0x06)], Drain().Select(Ids));
    }

    [Fact]
    public void A_stray_magic_byte_with_a_long_length_does_not_hide_a_later_frame()
    {
        // 5A 01 00 claims a 256-byte frame; the real reply that follows must not wait behind it.
        _reader.Append(Bytes("5A 01 00 " + BatteryReply));

        Assert.Equal([(0x01, 0x08)], Drain().Select(Ids));
    }

    [Fact]
    public void A_stray_magic_byte_with_an_impossible_length_is_skipped()
    {
        _reader.Append(Bytes("5A 5A 5A " + Preamble));

        Assert.Equal([(0x01, 0x06)], Drain().Select(Ids));
    }

    [Fact]
    public void Drops_sliced_frames()
    {
        // Slice flag 01 (first slice) and a slice index byte, with a valid CRC.
        var body = Bytes("5A 00 06 01 00 01 08 01 00");
        var crc = HuaweiProtocol.Crc16Xmodem(body);
        _reader.Append([.. body, (byte)(crc >> 8), (byte)crc]);
        _reader.Append(Bytes(Preamble));

        Assert.Equal([(0x01, 0x06)], Drain().Select(Ids));
    }
}
