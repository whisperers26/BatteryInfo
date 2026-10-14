using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Rog;
using static BatteryInfo.Core.Tests.Vendors.Rog.FakeOmni;

namespace BatteryInfo.Core.Tests.Vendors.Rog;

public class RogChannelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(600);

    private readonly FakeHidBackend _backend = new();
    private readonly ManualTimeProvider _time = new();
    private readonly RogResponder _slot;

    public RogChannelTests() => _slot = new RogResponder(_backend.Add(0x0B05, 0x1ACE, 0xFF00, 1, 64, 64, mi: 2, outputReportId: 2));

    private RogChannel Channel(out IHidConnection connection)
    {
        connection = _backend.Open(_slot.Device.Info, HidAccess.ReadWrite);
        return new RogChannel(connection, 0x02, _time);
    }

    [Fact]
    public void Reply_is_returned_with_one_write()
    {
        _slot.On(RogProtocol.KeyboardPowerInfo, KeyboardPower45);
        var channel = Channel(out var connection);
        using var _ = connection;

        var exchange = channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout);

        Assert.Equal(RogOutcome.Reply, exchange.Outcome);
        Assert.Equal(KeyboardPower45, exchange.Report);
        Assert.Single(_slot.Device.Written);
    }

    [Fact]
    public void Reject_is_final_and_not_retried()
    {
        _slot.On(RogProtocol.KeyboardPowerInfo, R(0x02, 0xFF, 0xAA));
        var channel = Channel(out var connection);
        using var _ = connection;

        Assert.Equal(RogOutcome.Rejected, channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout).Outcome);
        Assert.Single(_slot.Device.Written);
    }

    [Fact]
    public void Reject_followed_by_our_reply_is_the_reply()
    {
        // Another program's request was rejected (rog.md 5e); a reject echoes no header, so it may not be ours.
        _slot.On(RogProtocol.KeyboardPowerInfo, R(0x02, 0xFF, 0xAA), KeyboardPower45);
        var channel = Channel(out var connection);
        using var _ = connection;

        var exchange = channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout);

        Assert.Equal(RogOutcome.Reply, exchange.Outcome);
        Assert.Equal(KeyboardPower45, exchange.Report);
        Assert.Single(_slot.Device.Written);
    }

    [Fact]
    public void Reject_then_foreign_traffic_until_the_deadline_is_rejected_without_a_retry()
    {
        using var connection = new ChattyConnection(_slot.Device.Info, _time, first: R(0x02, 0xFF, 0xAA));
        var channel = new RogChannel(connection, 0x02, _time);

        var exchange = channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout);

        Assert.Equal(RogOutcome.Rejected, exchange.Outcome);
        Assert.Equal(R(0x02, 0xFF, 0xAA), exchange.Report);
        Assert.Equal(1, connection.Writes);
        Assert.InRange(connection.Reads, 6, 7);
    }

    [Fact]
    public void No_reply_is_retried_once()
    {
        var channel = Channel(out var connection);
        using var _ = connection;

        var exchange = channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout);

        Assert.Equal(RogOutcome.NoReply, exchange.Outcome);
        Assert.Null(exchange.Report);
        Assert.Equal(2, _slot.Device.Written.Count);
    }

    [Fact]
    public void Duplicate_header_within_100_ms_of_the_last_reply_is_ignored()
    {
        _slot.On(RogProtocol.KeyboardPowerInfo, KeyboardPower45);
        var channel = Channel(out var connection);
        using var _ = connection;
        channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout);
        _time.Advance(TimeSpan.FromMilliseconds(50));

        var second = channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout);

        Assert.Equal(RogOutcome.NoReply, second.Outcome);
    }

    [Fact]
    public void Same_header_after_100_ms_is_a_new_reply()
    {
        _slot.OnSequence(RogProtocol.KeyboardPowerInfo, [KeyboardPower45], [KeyboardPower(0x02, 44, charging: 0)]);
        var channel = Channel(out var connection);
        using var _ = connection;
        channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout);
        _time.Advance(TimeSpan.FromMilliseconds(150));

        var second = channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout);

        Assert.Equal(RogOutcome.Reply, second.Outcome);
        Assert.Equal(44, RogProtocol.ParseKeyboardPower(second.Report)!.Value.Percent);
    }

    [Fact]
    public void A_different_header_right_after_a_reply_is_accepted()
    {
        _slot.On(RogProtocol.KeyboardConnectionType, ConnectionRf).On(RogProtocol.KeyboardPowerInfo, KeyboardPower45);
        var channel = Channel(out var connection);
        using var _ = connection;
        channel.Transact(RogProtocol.KeyboardConnectionType, Timeout);

        Assert.Equal(RogOutcome.Reply, channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout).Outcome);
    }

    [Fact]
    public void Endless_foreign_traffic_stops_at_the_deadline()
    {
        using var connection = new ChattyConnection(_slot.Device.Info, _time);
        var channel = new RogChannel(connection, 0x02, _time);

        var exchange = channel.Transact(RogProtocol.KeyboardPowerInfo, Timeout);

        Assert.Equal(RogOutcome.NoReply, exchange.Outcome);
        Assert.Equal(2, connection.Writes);
        Assert.InRange(connection.Reads, 2, 14);
    }

    // Every read takes 100 ms and returns another program's reply, forever (the first read returns "first" when given).
    private sealed class ChattyConnection(HidDeviceInfo device, ManualTimeProvider time, byte[]? first = null) : IHidConnection
    {
        public int Writes { get; private set; }

        public int Reads { get; private set; }

        public HidDeviceInfo Device => device;

        public void Write(ReadOnlySpan<byte> report) => Writes++;

        public byte[]? Read(TimeSpan timeout)
        {
            Reads++;
            time.Advance(TimeSpan.FromMilliseconds(100));
            return Reads == 1 && first is not null ? first : R(0x02, 0x12, 0x07, 0x00, 0x00, 0x01);
        }

        public void SetFeature(ReadOnlySpan<byte> report) => throw new NotSupportedException();

        public void GetFeature(Span<byte> report) => throw new NotSupportedException();

        public void FlushQueue()
        {
        }

        public void Dispose()
        {
        }
    }
}
