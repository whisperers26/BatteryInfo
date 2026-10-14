using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.SteelSeries;

namespace BatteryInfo.Core.Tests.Vendors.SteelSeries;

public class SteelSeriesBatteryProviderTests
{
    private static readonly byte[] Request = SteelSeriesProtocolTests.Pad65(0x00, 0xB0);
    private static readonly byte[] Reply45 = SteelSeriesProtocolTests.OnlineReply;

    private readonly FakeHidBackend _fake = new();
    private readonly ManualTimeProvider _time = new();

    // The Nova 5X base station as it enumerates on this PC: the status collection plus two others.
    private (FakeHidDevice Status, FakeHidDevice[] Others) AddBaseStation(ushort pid = 0x2253)
    {
        var status = _fake.Add(0x1038, pid, 0xFFC0, 0x0001, 65, 65, feature: 515, mi: 3, outputReportId: 0x00);
        var keys = _fake.Add(0x1038, pid, 0x000C, 0x0001, 5, 0, mi: 4);
        var other = _fake.Add(0x1038, pid, 0xFF00, 0x0001, 65, 65, mi: 5, outputReportId: 0x00);
        return (status, [keys, other]);
    }

    private static void ReplyWith(FakeHidDevice device, params byte[][] reports) =>
        device.OnWrite = _ =>
        {
            foreach (var report in reports) device.Enqueue(report);
        };

    private static byte[] WithByte(byte[] report, int index, byte value)
    {
        var copy = (byte[])report.Clone();
        copy[index] = value;
        return copy;
    }

    private SteelSeriesBatteryProvider Provider() => new(_fake, _time);

    private IReadOnlyList<DeviceReading> Poll() => Provider().PollAsync(CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public void Vendor_is_SteelSeries() => Assert.Equal("SteelSeries", Provider().Vendor);

    [Fact]
    public void Connected_headset_reports_its_battery()
    {
        var (status, _) = AddBaseStation();
        ReplyWith(status, Reply45);

        var reading = Assert.Single(Poll());

        Assert.Equal("steelseries:2253", reading.DeviceId);
        Assert.Equal("SteelSeries", reading.Vendor);
        Assert.Equal("SteelSeries Arctis Nova 5X", reading.Name);
        Assert.Equal(DeviceKind.Headset, reading.Kind);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(45, ChargeState.Discharging)], reading.Components);
        Assert.Equal(_time.GetUtcNow(), reading.Timestamp);
        Assert.Equal("SteelSeries 0xB0", reading.Source);
        Assert.Null(reading.Detail);
    }

    [Fact]
    public void Exactly_one_status_request_is_written_to_the_status_collection_only()
    {
        var (status, others) = AddBaseStation();
        ReplyWith(status, Reply45);

        Poll();

        Assert.Equal([Request], status.Written);
        Assert.Equal([HidAccess.ReadWrite], status.Opens);
        Assert.All(others, o => Assert.Empty(o.Opens));
    }

    [Fact]
    public void Charging_headset_reports_charging()
    {
        var (status, _) = AddBaseStation();
        ReplyWith(status, WithByte(Reply45, 5, 0x01));

        Assert.Equal(ChargeState.Charging, Assert.Single(Poll()).Components.Single().Charge);
    }

    [Fact]
    public void Headset_off_is_asleep_without_a_stale_percent()
    {
        var (status, _) = AddBaseStation();
        ReplyWith(status, WithByte(Reply45, 2, 0x02));

        var reading = Assert.Single(Poll());

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Equal("Headset off or out of range", reading.Detail);
    }

    [Fact]
    public void Foreign_frames_are_skipped()
    {
        var (status, _) = AddBaseStation();
        ReplyWith(status, SteelSeriesProtocolTests.Pad65(0x00, 0xB1, 0x07), Reply45);

        Assert.Equal(DeviceStatus.Online, Assert.Single(Poll()).Status);
    }

    [Fact]
    public void No_answer_retries_once_then_reports_asleep()
    {
        var (status, _) = AddBaseStation();

        var reading = Assert.Single(Poll());

        Assert.Equal(2, status.Written.Count);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Equal("No answer from the base station", reading.Detail);
    }

    [Fact]
    public void Access_denied_needs_access()
    {
        var (status, _) = AddBaseStation();
        status.OpenError = new HidIoException("Cannot open", HidIoException.AccessDenied);

        Assert.Equal(DeviceStatus.NeedsAccess, Assert.Single(Poll()).Status);
    }

    [Fact]
    public void Base_station_without_its_status_collection_is_an_error()
    {
        _fake.Add(0x1038, 0x2253, 0x000C, 0x0001, 5, 0, mi: 4);

        var reading = Assert.Single(Poll());

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Battery interface not found", reading.Detail);
    }

    [Fact]
    public void Nova_5_base_station_is_read_too()
    {
        var (status, _) = AddBaseStation(0x2232);
        ReplyWith(status, Reply45);

        Assert.Equal("SteelSeries Arctis Nova 5", Assert.Single(Poll()).Name);
    }

    [Fact]
    public void Other_SteelSeries_devices_are_never_opened()
    {
        var mouse = _fake.Add(0x1038, 0x1836, 0xFFC0, 0x0001, 65, 65);

        Assert.Empty(Poll());
        Assert.Empty(mouse.Opens);
    }
}
