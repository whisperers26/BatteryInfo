using System.Net.Sockets;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Safety;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Huawei;
using static BatteryInfo.Core.Tests.Vendors.Huawei.HuaweiFrameReaderTests;
using static BatteryInfo.Core.Tests.Vendors.Huawei.HuaweiProtocolTests;

namespace BatteryInfo.Core.Tests.Vendors.Huawei;

public class HuaweiBatteryProviderTests
{
    private const ulong Address = 0xC0DA5EA83EC7;
    private const ulong OtherAddress = 0x112233445566;
    private static readonly byte[] Request = Bytes("5A 00 09 00 01 08 01 00 02 00 03 00 FB B9");

    private readonly ManualTimeProvider _time = new();
    private readonly FakeHuaweiTransport _transport = new();
    private readonly List<FakeSppStream> _streams = [];

    public HuaweiBatteryProviderTests()
    {
        _transport.Paired.Add(new HuaweiDevice(Address, "HUAWEI FreeClip 2"));
        _transport.Connected.Add(Address);
        _transport.WindowsBattery[Address] = 80;
    }

    private HuaweiBatteryProvider CreateProvider() => new(_transport, _time);

    /// <summary>Every open returns a new stream that sends the connect preamble and answers each write with
    /// <paramref name="reply"/> (nothing when null).</summary>
    private void Earbuds(Func<byte[], IEnumerable<byte[]>>? reply)
    {
        _transport.OnOpen = _ =>
        {
            var stream = new FakeSppStream(_time) { OnWrite = reply };
            stream.Deliver(Bytes(Preamble));
            _streams.Add(stream);
            return stream;
        };
    }

    private async Task<DeviceReading> PollOne()
    {
        using var provider = CreateProvider();
        var readings = await provider.PollAsync(CancellationToken.None);
        return Assert.Single(readings);
    }

    [Fact]
    public async Task Connected_earbuds_report_left_right_and_case_over_spp()
    {
        Earbuds(_ => [Bytes(BatteryReply), Bytes(Hello)]);

        var reading = await PollOne();

        Assert.Equal("huawei:C0DA5EA83EC7", reading.DeviceId);
        Assert.Equal("HUAWEI", reading.Vendor);
        Assert.Equal("HUAWEI FreeClip 2", reading.Name);
        Assert.Equal(DeviceKind.Earbuds, reading.Kind);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal("SPP", reading.Source);
        Assert.False(reading.Approximate);
        Assert.Null(reading.Detail);
        Assert.Equal(_time.GetUtcNow(), reading.Timestamp);
        Assert.Equal(
            [
                new BatteryComponent("Left", 76, ChargeState.Discharging),
                new BatteryComponent("Right", 75, ChargeState.Discharging),
                new BatteryComponent("Case", 100, ChargeState.Discharging),
            ],
            reading.Components);
    }

    [Fact]
    public async Task Sends_only_the_battery_request_and_closes_the_link()
    {
        Earbuds(_ => [Bytes(BatteryReply)]);

        await PollOne();

        var stream = Assert.Single(_streams);
        Assert.Equal([Request], stream.Written);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task Finds_the_reply_behind_a_stray_magic_byte()
    {
        Earbuds(_ => [Bytes(Preamble + "5A 01 00"), Bytes(BatteryReply)]);

        var reading = await PollOne();

        Assert.Equal("SPP", reading.Source);
        Assert.Single(_streams[0].Written);
    }

    [Fact]
    public async Task Reassembles_a_reply_split_across_reads()
    {
        var reply = Bytes(BatteryReply);
        Earbuds(_ => [Bytes("00 13 " + Preamble + DualConnect), reply[..5], reply[5..20], reply[20..], Bytes(Hello)]);

        var reading = await PollOne();

        Assert.Equal("SPP", reading.Source);
        Assert.Equal(76, reading.Components[0].Percent);
    }

    [Fact]
    public async Task Accepts_a_battery_changed_notification_with_the_same_tlvs()
    {
        var reply = Bytes(BatteryReply);
        var notification = HuaweiProtocol.BuildFrame(0x01, 0x27, reply.AsSpan(6, reply.Length - 8));
        Earbuds(_ => [notification]);

        var reading = await PollOne();

        Assert.Equal("SPP", reading.Source);
        Assert.Equal(3, reading.Components.Count);
    }

    [Fact]
    public async Task Disconnected_earbuds_are_never_paged_and_report_nothing()
    {
        Earbuds(_ => [Bytes(BatteryReply)]);
        _transport.Connected.Clear();
        using var provider = CreateProvider();

        var readings = await provider.PollAsync(CancellationToken.None);

        Assert.Empty(readings);
        Assert.Empty(_transport.Opens);
    }

    [Fact]
    public async Task Only_connected_earbuds_are_read()
    {
        Earbuds(_ => [Bytes(BatteryReply)]);
        _transport.Paired.Add(new HuaweiDevice(OtherAddress, "HUAWEI FreeBuds Pro 3"));
        using var provider = CreateProvider();

        var readings = await provider.PollAsync(CancellationToken.None);

        Assert.Equal("huawei:C0DA5EA83EC7", Assert.Single(readings).DeviceId);
        Assert.Equal([Address], _transport.Opens);
    }

    [Fact]
    public async Task Busy_channel_falls_back_to_the_windows_value()
    {
        _transport.OnOpen = _ => throw new SocketException((int)SocketError.AddressAlreadyInUse);

        var reading = await PollOne();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.True(reading.Approximate);
        Assert.Equal("HFP (approx.)", reading.Source);
        Assert.Equal([BatteryComponent.Single(80, ChargeState.Unknown)], reading.Components);
        Assert.Contains("another app", reading.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_timeout_falls_back_to_the_windows_value()
    {
        _transport.OnOpen = _ => throw new TimeoutException();

        var reading = await PollOne();

        Assert.Equal("HFP (approx.)", reading.Source);
        Assert.True(reading.Approximate);
        Assert.Contains("connect", reading.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_reply_asks_once_more_then_falls_back_to_the_windows_value()
    {
        Earbuds(null);

        var reading = await PollOne();

        Assert.Equal([Request, Request], Assert.Single(_streams).Written);
        Assert.Equal("HFP (approx.)", reading.Source);
        Assert.True(reading.Approximate);
        Assert.Contains("reply", reading.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_reply_to_the_second_request_is_used()
    {
        var writes = 0;
        Earbuds(_ => ++writes == 2 ? [Bytes(BatteryReply)] : []);

        var reading = await PollOne();

        Assert.Equal("SPP", reading.Source);
        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task No_reply_and_no_windows_value_is_asleep()
    {
        Earbuds(null);
        _transport.WindowsBattery.Clear();

        var reading = await PollOne();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.False(reading.Approximate);
        Assert.NotNull(reading.Detail);
    }

    [Fact]
    public async Task Busy_channel_and_no_windows_value_is_an_error_with_detail()
    {
        _transport.OnOpen = _ => throw new SocketException((int)SocketError.AddressAlreadyInUse);
        _transport.WindowsBattery.Clear();

        var reading = await PollOne();

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Contains("another app", reading.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Link_closed_mid_read_falls_back()
    {
        Earbuds(_ => [[]]);

        var reading = await PollOne();

        Assert.Equal("HFP (approx.)", reading.Source);
        Assert.True(Assert.Single(_streams).Disposed);
    }

    [Fact]
    public async Task Read_failure_falls_back()
    {
        Earbuds(null);
        _transport.OnOpen = _ =>
        {
            var stream = new FakeSppStream(_time) { ReadError = new IOException("link lost") };
            _streams.Add(stream);
            return stream;
        };

        var reading = await PollOne();

        Assert.Equal("HFP (approx.)", reading.Source);
    }

    [Fact]
    public async Task Unreadable_battery_reply_falls_back()
    {
        Earbuds(_ => [HuaweiProtocol.BuildFrame(0x01, 0x08, [0x7F, 0x04, 0x00, 0x01, 0x86, 0xA1])]);

        var reading = await PollOne();

        Assert.Equal("HFP (approx.)", reading.Source);
        Assert.Single(_streams[0].Written);
    }

    [Fact]
    public async Task Windows_value_above_100_is_ignored()
    {
        Earbuds(null);
        _transport.WindowsBattery[Address] = 255;

        var reading = await PollOne();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
    }

    [Fact]
    public async Task Refused_frame_is_an_internal_error_without_fallback()
    {
        Earbuds(null);
        _transport.OnOpen = _ =>
        {
            var stream = new FakeSppStream(_time) { WriteError = new UnsafeCommandException("refused") };
            _streams.Add(stream);
            return stream;
        };

        var reading = await PollOne();

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Internal error: unsafe command refused", reading.Detail);
        Assert.Empty(reading.Components);
    }

    [Fact]
    public async Task Failing_device_discovery_reports_nothing()
    {
        _transport.FindError = new InvalidOperationException("no radio");
        using var provider = CreateProvider();

        var readings = await provider.PollAsync(CancellationToken.None);

        Assert.Empty(readings);
    }

    [Fact]
    public async Task Cancelled_poll_throws_without_touching_the_earbuds()
    {
        Earbuds(_ => [Bytes(BatteryReply)]);
        using var provider = CreateProvider();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.PollAsync(new CancellationToken(canceled: true)));

        Assert.Empty(_transport.Opens);
    }

    [Fact]
    public void Connection_changes_request_a_refresh()
    {
        using var provider = CreateProvider();
        var requests = 0;
        provider.RefreshRequested += (_, _) => requests++;

        _transport.RaiseConnectionChanged();

        Assert.Equal(1, requests);
    }

    [Fact]
    public void Dispose_unsubscribes_and_disposes_the_transport()
    {
        var provider = CreateProvider();

        provider.Dispose();

        Assert.False(_transport.HasConnectionListeners);
        Assert.True(_transport.Disposed);
    }

    [Fact]
    public void Vendor_is_huawei()
    {
        using var provider = CreateProvider();

        Assert.Equal("HUAWEI", provider.Vendor);
    }
}
