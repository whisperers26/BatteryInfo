using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Logitech;

namespace BatteryInfo.Core.Tests.Vendors.Logitech;

public class HidppChannelTests
{
    private const string ParentA = @"USB\VID_046D&PID_C54D&MI_02\B&18153BD2&0&0002";
    private const string ParentB = @"USB\VID_046D&PID_C54D&MI_02\B&99999999&0&0003";
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private readonly FakeHidBackend _backend = new();

    private static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    private static byte[] Long(string hex)
    {
        var report = new byte[HidppProtocol.LongLength];
        Bytes(hex).CopyTo(report, 0);
        return report;
    }

    private (FakeHidDevice Short, FakeHidDevice Long) AddReceiver(string parent) =>
        (_backend.Add(0x046D, 0xC54D, 0xFF00, 0x0001, 7, 7, mi: 2, parent: parent),
         _backend.Add(0x046D, 0xC54D, 0xFF00, 0x0002, 20, 20, mi: 2, parent: parent));

    private HidppChannel Open(FakeHidDevice shortDevice, FakeHidDevice longDevice) =>
        new(_backend.Open(shortDevice.Info, HidAccess.ReadWrite), _backend.Open(longDevice.Info, HidAccess.ReadWrite), TimeSpan.FromMilliseconds(1));

    [Fact]
    public void Pairs_the_short_and_long_collections_of_each_receiver()
    {
        _backend.Add(0x046D, 0xC54D, 0x0001, 0x0002, 8, 0, mi: 0, accessible: false);
        var a = AddReceiver(ParentA);
        _backend.Add(0x046D, 0xC54D, 0x000C, 0x0001, 3, 0, mi: 1, parent: @"USB\VID_046D&PID_C54D&MI_01\B&1");
        var b = AddReceiver(ParentB);

        var channels = HidppChannel.Pair(_backend.Enumerate());

        Assert.Equal(2, channels.Count);
        Assert.Equal((a.Short.Info, a.Long.Info), (channels[0].Short, channels[0].Long));
        Assert.Equal((b.Short.Info, b.Long.Info), (channels[1].Short, channels[1].Long));
        Assert.Equal(LogitechCommandPolicy.ChannelKey(a.Short.Info), channels[0].Key);
    }

    [Fact]
    public void A_receiver_without_both_collections_is_not_a_channel()
    {
        _backend.Add(0x046D, 0xC54D, 0xFF00, 0x0001, 7, 7, mi: 2, parent: ParentA);
        _backend.Add(0x046D, 0xC54D, 0xFF00, 0x0002, 64, 64, mi: 2, parent: ParentA);
        _backend.Add(0x046D, 0xC54D, 0xFF00, 0x0002, 20, 20, mi: 2, parent: ParentB);

        Assert.Empty(HidppChannel.Pair(_backend.Enumerate()));
    }

    [Fact]
    public void Inaccessible_collections_are_not_paired()
    {
        _backend.Add(0x046D, 0xC54D, 0xFF00, 0x0001, 7, 7, mi: 2, parent: ParentA, accessible: false);
        _backend.Add(0x046D, 0xC54D, 0xFF00, 0x0002, 20, 20, mi: 2, parent: ParentA);

        Assert.Empty(HidppChannel.Pair(_backend.Enumerate()));
    }

    [Fact]
    public void Request_goes_to_the_short_collection_and_the_reply_is_read_from_the_long_one()
    {
        var (s, l) = AddReceiver(ParentA);
        s.OnWrite = _ => l.Enqueue(Long("11 01 00 1C 04 02 A2"));
        using var channel = Open(s, l);

        var result = channel.Send(HidppProtocol.Ping(0x01, 0x0C, 0xA2), Timeout);

        Assert.Equal(HidppMatchKind.Reply, result.Kind);
        Assert.Equal(Long("11 01 00 1C 04 02 A2"), result.Report);
        Assert.Single(s.Written);
        Assert.Empty(l.Written);
    }

    [Fact]
    public void Foreign_replies_and_notifications_are_skipped_and_reported_as_unsolicited()
    {
        var (s, l) = AddReceiver(ParentA);
        byte[][] foreign = [Long("11 01 00 1D 04 02 A2"), Long("11 01 00 1C 04 02 A1"), Long("11 01 04 00 01 01 01")];
        s.OnWrite = _ =>
        {
            foreach (var report in foreign) l.Enqueue(report);
            s.Enqueue(Bytes("10 01 41 0C 02 BD 40"));
            l.Enqueue(Long("11 01 00 1C 04 02 A2"));
        };
        using var channel = Open(s, l);
        var unsolicited = new List<byte[]>();
        channel.Unsolicited += unsolicited.Add;

        var result = channel.Send(HidppProtocol.Ping(0x01, 0x0C, 0xA2), Timeout);

        Assert.Equal(HidppMatchKind.Reply, result.Kind);
        Assert.Equal(4, unsolicited.Count);
        Assert.Contains(unsolicited, r => r[2] == 0x41);
    }

    [Fact]
    public void Receiver_error_on_the_short_collection_answers_the_request()
    {
        var (s, l) = AddReceiver(ParentA);
        s.OnWrite = _ => s.Enqueue(Bytes("10 01 8F 00 1C 09 00"));
        using var channel = Open(s, l);

        var result = channel.Send(HidppProtocol.Ping(0x01, 0x0C, 0x33), Timeout);

        Assert.Equal(HidppMatchKind.Hidpp10Error, result.Kind);
        Assert.Equal(0x09, result.ErrorCode);
    }

    [Fact]
    public void Register_reply_on_the_short_collection_is_found()
    {
        var (s, l) = AddReceiver(ParentA);
        s.OnWrite = _ => s.Enqueue(Bytes("10 01 81 0D 4B 00 30"));
        using var channel = Open(s, l);

        var result = channel.Send(HidppProtocol.ReadRegister(0x01, 0x0D), Timeout);

        Assert.Equal(HidppMatchKind.Reply, result.Kind);
    }

    [Fact]
    public void No_answer_times_out()
    {
        var (s, l) = AddReceiver(ParentA);
        using var channel = Open(s, l);

        var result = channel.Send(HidppProtocol.Ping(0x01, 0x0C, 0x33), Timeout);

        Assert.True(result.TimedOut);
        Assert.Null(result.Report);
    }

    [Fact]
    public void Device_gone_while_reading_propagates()
    {
        var (s, l) = AddReceiver(ParentA);
        l.ReadError = new HidIoException("ReadFile", 1167);
        using var channel = Open(s, l);

        var error = Assert.Throws<HidIoException>(() => channel.Send(HidppProtocol.Ping(0x01, 0x0C, 0x33), Timeout));
        Assert.True(error.DeviceGone);
    }
}
