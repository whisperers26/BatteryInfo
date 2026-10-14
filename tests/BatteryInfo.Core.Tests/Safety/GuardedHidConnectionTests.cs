using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Safety;
using BatteryInfo.Core.Tests.Fakes;

namespace BatteryInfo.Core.Tests.Safety;

public class GuardedHidConnectionTests
{
    // Allows only frames whose second byte is 0x15 (a read in this made-up protocol).
    private sealed class OnlyReads : ICommandPolicy
    {
        public bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing) => outgoing.Length > 1 && outgoing[1] == 0x15;
    }

    private readonly FakeHidBackend _backend = new();
    private readonly FakeHidDevice _device;

    public GuardedHidConnectionTests() => _device = _backend.Add(0x20BC, 0x518D, 0x00FF, 0x0003, 64, 64, feature: 8);

    private GuardedHidConnection Open() => new(_backend.Open(_device.Info, HidAccess.ReadWrite), new OnlyReads());

    private static byte[] Frame(byte command, int length = 64)
    {
        var frame = new byte[length];
        frame[0] = 0x02;
        frame[1] = command;
        return frame;
    }

    [Fact]
    public void Allowed_write_reaches_the_device()
    {
        using var connection = Open();

        connection.Write(Frame(0x15));

        Assert.Single(_device.Written);
    }

    [Fact]
    public void Refused_write_throws_and_sends_nothing()
    {
        using var connection = Open();

        Assert.Throws<UnsafeCommandException>(() => connection.Write(Frame(0x14)));

        Assert.Empty(_device.Written);
    }

    [Fact]
    public void Refused_set_feature_throws_and_sends_nothing()
    {
        using var connection = Open();

        Assert.Throws<UnsafeCommandException>(() => connection.SetFeature(Frame(0x14, 8)));

        Assert.Empty(_device.FeaturesSet);
    }

    [Fact]
    public void Allowed_set_feature_reaches_the_device()
    {
        using var connection = Open();

        connection.SetFeature(Frame(0x15, 8));

        Assert.Single(_device.FeaturesSet);
    }

    [Fact]
    public void Reads_pass_through()
    {
        _device.Enqueue(0x02, 0x15, 0x5B);
        using var connection = Open();

        Assert.Equal(new byte[] { 0x02, 0x15, 0x5B }, connection.Read(TimeSpan.FromMilliseconds(10)));
    }
}
