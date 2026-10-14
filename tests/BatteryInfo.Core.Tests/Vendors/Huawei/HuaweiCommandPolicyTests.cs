using BatteryInfo.Core.Safety;
using BatteryInfo.Core.Vendors.Huawei;
using static BatteryInfo.Core.Tests.Vendors.Huawei.HuaweiProtocolTests;

namespace BatteryInfo.Core.Tests.Vendors.Huawei;

public class HuaweiCommandPolicyTests
{
    private const string Request = "5A 00 09 00 01 08 01 00 02 00 03 00 FB B9";

    [Fact]
    public void Allows_the_battery_request() => Assert.True(HuaweiCommandPolicy.IsAllowed(HuaweiProtocol.BuildBatteryRequest()));

    [Fact]
    public void Allows_the_short_battery_request_with_only_tlv_1() =>
        Assert.True(HuaweiCommandPolicy.IsAllowed(Bytes("5A 00 05 00 01 08 01 00 F5 3D")));

    public static TheoryData<string, byte[]> Refused => new()
    {
        { "battery command carrying a value", HuaweiProtocol.BuildFrame(0x01, 0x08, [0x01, 0x01, 0x4B]) },
        { "battery command with an extra TLV", HuaweiProtocol.BuildFrame(0x01, 0x08, [0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00]) },
        { "0x011F", HuaweiProtocol.BuildFrame(0x01, 0x1F, [0x01, 0x00]) },
        { "0x0125", HuaweiProtocol.BuildFrame(0x01, 0x25, [0x01, 0x00]) },
        { "0x0127 notification id", HuaweiProtocol.BuildFrame(0x01, 0x27, [0x01, 0x00, 0x02, 0x00, 0x03, 0x00]) },
        { "0x0106", HuaweiProtocol.BuildFrame(0x01, 0x06, []) },
        { "0x2B04 setting write", HuaweiProtocol.BuildFrame(0x2B, 0x04, [0x01, 0x01, 0x01]) },
        { "0x2BAC", HuaweiProtocol.BuildFrame(0x2B, 0xAC, [0x01, 0x00]) },
        { "request with a bad CRC", Bytes("5A 00 09 00 01 08 01 00 02 00 03 00 FB B8") },
        { "request with a trailing byte", Bytes(Request + " 00") },
        { "truncated request", Bytes("5A 00 09 00 01 08 01 00 02 00 03 00 FB") },
        { "two requests in one write", Bytes(Request + Request) },
        { "sliced request", Sliced() },
        { "empty", [] },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Refuses_everything_else(string name, byte[] frame) => Assert.False(HuaweiCommandPolicy.IsAllowed(frame), name);

    [Fact]
    public void Ensure_allowed_throws_for_a_refused_frame() =>
        Assert.Throws<UnsafeCommandException>(() => HuaweiCommandPolicy.EnsureAllowed(HuaweiProtocol.BuildFrame(0x2B, 0x04, [0x01, 0x01, 0x01])));

    [Fact]
    public void Guarded_stream_passes_the_battery_request()
    {
        var inner = new MemoryStream();
        using var stream = new GuardedSppStream(inner);

        stream.Write(HuaweiProtocol.BuildBatteryRequest());

        Assert.Equal(Bytes(Request), inner.ToArray());
    }

    [Fact]
    public void Guarded_stream_refuses_other_frames_and_sends_nothing()
    {
        var inner = new MemoryStream();
        using var stream = new GuardedSppStream(inner);
        var write = HuaweiProtocol.BuildFrame(0x2B, 0x04, [0x01, 0x01, 0x01]);

        Assert.Throws<UnsafeCommandException>(() => stream.Write(write));
        Assert.Throws<UnsafeCommandException>(() => stream.Write(write, 0, write.Length));
        Assert.Throws<UnsafeCommandException>(() => stream.WriteByte(0x5A));
        Assert.Throws<UnsafeCommandException>(() => stream.WriteAsync(write).AsTask().GetAwaiter().GetResult());
        Assert.Throws<UnsafeCommandException>(() => stream.WriteAsync(write, 0, write.Length).GetAwaiter().GetResult());

        Assert.Equal(0, inner.Length);
    }

    [Fact]
    public void Guarded_stream_refuses_a_request_split_across_writes()
    {
        var inner = new MemoryStream();
        using var stream = new GuardedSppStream(inner);
        var request = HuaweiProtocol.BuildBatteryRequest();

        Assert.Throws<UnsafeCommandException>(() => stream.Write(request, 0, 6));

        Assert.Equal(0, inner.Length);
    }

    [Fact]
    public void Guarded_stream_reads_through_and_disposes_the_inner_stream()
    {
        var inner = new MemoryStream([1, 2, 3]);
        var stream = new GuardedSppStream(inner);
        var buffer = new byte[8];

        Assert.Equal(3, stream.Read(buffer, 0, buffer.Length));
        stream.Dispose();

        Assert.False(inner.CanRead);
    }

    private static byte[] Sliced()
    {
        var body = Bytes("5A 00 0A 01 00 01 08 01 00 02 00 03 00");
        var crc = HuaweiProtocol.Crc16Xmodem(body);
        return [.. body, (byte)(crc >> 8), (byte)crc];
    }
}
