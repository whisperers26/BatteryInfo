using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Betop;

namespace BatteryInfo.Core.Tests.Vendors.Betop;

public class BetopBatteryProviderTests
{
    private static readonly byte[] Request = BetopProtocolTests.Pad64(0x02, 0x15);
    private static readonly byte[] Reply91 = BetopProtocolTests.ObservedReply;

    private readonly FakeHidBackend _fake = new();
    private readonly ManualTimeProvider _time = new();

    // The KP70C2 dongle as it enumerates on this PC with the pad on: the vendor collection plus three others.
    private (FakeHidDevice Vendor, FakeHidDevice[] Others) AddDongle(ushort vid = 0x20BC, ushort pid = 0x518D)
    {
        var vendor = _fake.Add(vid, pid, 0x00FF, 0x0003, 64, 64, mi: 1, outputReportId: 0x02);
        var mouse = _fake.Add(vid, pid, 0, 0, 0, 0, mi: 1, accessible: false);
        var keyboard = _fake.Add(vid, pid, 0x0001, 0x0006, 27, 2, mi: 1);
        var xinput = _fake.Add(vid, pid, 0x0001, 0x0005, 15, 0, mi: 0);
        return (vendor, [mouse, keyboard, xinput]);
    }

    private static void ReplyWith(FakeHidDevice device, params byte[][] reports) =>
        device.OnWrite = _ =>
        {
            foreach (var report in reports) device.Enqueue(report);
        };

    private BetopBatteryProvider Provider(IHidBackend? backend = null) => new(backend ?? _fake, _time);

    private static IReadOnlyList<DeviceReading> Poll(BetopBatteryProvider provider) =>
        provider.PollAsync(CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public void Vendor_is_Betop() => Assert.Equal("Betop", Provider().Vendor);

    [Fact]
    public void Online_pad_reports_the_battery_from_the_vendor_collection()
    {
        var (vendor, _) = AddDongle();
        ReplyWith(vendor, Reply91);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal("betop:KP70C2", reading.DeviceId);
        Assert.Equal("Betop", reading.Vendor);
        Assert.Equal("Betop KP70C2", reading.Name);
        Assert.Equal(DeviceKind.Controller, reading.Kind);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(91, ChargeState.Discharging)], reading.Components);
        Assert.Equal(_time.GetUtcNow(), reading.Timestamp);
        Assert.Equal("Betop 0x15", reading.Source);
        Assert.False(reading.Approximate);
        Assert.Null(reading.Detail);
    }

    [Fact]
    public void Exactly_one_battery_request_is_written_to_the_vendor_collection_only()
    {
        var (vendor, others) = AddDongle();
        ReplyWith(vendor, Reply91);

        Poll(Provider());

        Assert.Equal([Request], vendor.Written);
        Assert.Equal([HidAccess.ReadWrite], vendor.Opens);
        Assert.All(others, o => Assert.Empty(o.Opens));
    }

    [Fact]
    public void Charging_pad_reports_charging()
    {
        var (vendor, _) = AddDongle();
        var charging = (byte[])Reply91.Clone();
        charging[4] = 0x51;
        ReplyWith(vendor, charging);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(ChargeState.Charging, Assert.Single(reading.Components).Charge);
    }

    // kp20d.js, kp20dl.js, kp20eb.js, kp40d.js, kp40df.js and kp40dk.js all mask byte 4 with 0x0F, like kp70c2.js.
    [Theory]
    [InlineData(0x5159, "betop:KP20D", 0x51, ChargeState.Charging)]
    [InlineData(0x5159, "betop:KP20D", 0x50, ChargeState.Discharging)]
    [InlineData(0x5170, "betop:KP20DL", 0x51, ChargeState.Charging)]
    [InlineData(0x5191, "betop:KP20EB", 0x51, ChargeState.Charging)]
    [InlineData(0x515C, "betop:KP40D", 0x51, ChargeState.Charging)]
    [InlineData(0x5166, "betop:KP40MCF", 0x51, ChargeState.Charging)]
    [InlineData(0x5166, "betop:KP40MCF", 0x50, ChargeState.Discharging)]
    [InlineData(0x5164, "betop:KP40MCK", 0x51, ChargeState.Charging)]
    public void KP20_and_KP40_dongles_read_the_charge_state_from_the_low_nibble(int pid, string deviceId, byte chargeByte,
        ChargeState expected)
    {
        var dongle = _fake.Add(0x20BC, (ushort)pid, 0x00FF, 0x0003, 64, 64, mi: 1, outputReportId: 0x02);
        var reply = (byte[])Reply91.Clone();
        reply[4] = chargeByte;
        ReplyWith(dongle, reply);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(deviceId, reading.DeviceId);
        Assert.Equal([BatteryComponent.Single(91, expected)], reading.Components);
    }

    [Fact]
    public void Foreign_frames_from_BetopGame_are_skipped()
    {
        var (vendor, _) = AddDongle();
        ReplyWith(vendor,
            BetopProtocolTests.Pad64(0x02, 0x25, 0x10, 0x00, 0x51),
            BetopProtocolTests.Pad64(0x02, 0x11, 0x00, 0x00, 0x51),
            BetopProtocolTests.Pad64(0x02, 0x21, 0x07, 0x00, 0x51),
            BetopProtocolTests.Pad64(0x02, 0x55, 0x01, 0x00, 0x03, 0x01),
            Reply91);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal(91, reading.LowestPercent);
        Assert.Single(vendor.Written);
    }

    [Fact]
    public void Stale_input_is_flushed_before_the_request()
    {
        var (vendor, _) = AddDongle();
        var stale = (byte[])Reply91.Clone();
        stale[2] = 50;
        vendor.Enqueue(stale);
        ReplyWith(vendor, Reply91);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(91, reading.LowestPercent);
    }

    [Fact]
    public void A_missed_reply_is_retried()
    {
        var (vendor, _) = AddDongle();
        var writes = 0;
        vendor.OnWrite = _ =>
        {
            if (++writes == 2) vendor.Enqueue(Reply91);
        };

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal(2, vendor.Written.Count);
    }

    [Fact]
    public void No_answer_after_two_retries_is_asleep()
    {
        var (vendor, _) = AddDongle();

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Equal("No answer from the controller", reading.Detail);
        Assert.Equal(3, vendor.Written.Count);
    }

    [Fact]
    public void Wired_pad_shares_the_device_id_with_the_dongle()
    {
        var wired = _fake.Add(0x20BC, 0x518C, 0x00FF, 0x0003, 64, 64, mi: 1);
        ReplyWith(wired, Reply91);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal("betop:KP70C2", reading.DeviceId);
        Assert.Equal(DeviceStatus.Online, reading.Status);
    }

    [Fact]
    public void Wired_and_dongle_paths_of_one_model_give_one_reading_preferring_the_cable()
    {
        var (dongle, _) = AddDongle();
        var wired = _fake.Add(0x20BC, 0x518C, 0x00FF, 0x0003, 64, 64, mi: 1);
        ReplyWith(wired, Reply91);
        ReplyWith(dongle, Reply91);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Single(wired.Written);
        Assert.Empty(dongle.Opens);
    }

    [Fact]
    public void Falls_back_to_the_dongle_when_the_cable_path_does_not_answer()
    {
        var (dongle, _) = AddDongle();
        var wired = _fake.Add(0x20BC, 0x518C, 0x00FF, 0x0003, 64, 64, mi: 1);
        ReplyWith(dongle, Reply91);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal(3, wired.Written.Count);
        Assert.Single(dongle.Written);
    }

    [Fact]
    public void Model_without_an_accessible_vendor_collection_is_an_error_and_nothing_is_opened()
    {
        var keyboard = _fake.Add(0x20BC, 0x518D, 0x0001, 0x0006, 27, 2, mi: 1);
        var vendor = _fake.Add(0x20BC, 0x518D, 0, 0, 0, 0, mi: 1, accessible: false);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Battery interface not found", reading.Detail);
        Assert.Empty(keyboard.Opens);
        Assert.Empty(vendor.Opens);
    }

    [Fact]
    public void Other_vendors_and_byte_substituting_models_are_never_enumerated_or_opened()
    {
        var kp20a = _fake.Add(0x20BC, 0x5126, 0x00FF, 0x0003, 64, 64, mi: 1);
        var logitech = _fake.Add(0x046D, 0xC54D, 0xFF00, 0x0001, 7, 7, mi: 2);
        var kp50Updater = _fake.Add(0x0517, 0x5350, 0xFF00, 0x0001, 64, 64);
        var backend = new TrackingHidBackend(_fake);

        Assert.Empty(Poll(Provider(backend)));

        Assert.All(backend.FilterCalls.Where(c => c.VendorId is not 0x20BC), c => Assert.False(c.Accepted));
        Assert.Empty(kp20a.Opens);
        Assert.Empty(logitech.Opens);
        Assert.Empty(kp50Updater.Opens);
    }

    [Fact]
    public void No_devices_no_readings() => Assert.Empty(Poll(Provider()));

    [Fact]
    public void Asleep_dongle_20DD_is_reported_asleep_without_being_opened()
    {
        // 20DD:518D exposes a vendor collection identical to the 20BC one; only the VID tells them apart.
        var asleep = _fake.Add(0x20DD, 0x518D, 0x00FF, 0x0003, 64, 64, mi: 1, outputReportId: 0x02);
        var backend = new TrackingHidBackend(_fake);

        var reading = Assert.Single(Poll(Provider(backend)));

        Assert.Equal("betop:KP70C2", reading.DeviceId);
        Assert.Equal("Betop KP70C2", reading.Name);
        Assert.Equal(DeviceKind.Controller, reading.Kind);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Equal(_time.GetUtcNow(), reading.Timestamp);
        Assert.Equal("Controller asleep or off", reading.Detail);
        Assert.Empty(asleep.Opens);
        Assert.Equal(0, asleep.SentCount);
        // Rejected by the enumeration filter, so the backend does not even open it with zero access for its caps.
        Assert.Contains((0x20DD, 0x518D, false), backend.FilterCalls.Select(c => ((int)c.VendorId, (int)c.ProductId, c.Accepted)));
    }

    [Fact]
    public void Asleep_dongle_is_not_opened_even_when_the_backend_ignores_the_filter()
    {
        var asleep = _fake.Add(0x20DD, 0x518D, 0x00FF, 0x0003, 64, 64, mi: 1, outputReportId: 0x02);

        var reading = Assert.Single(Poll(Provider(new TrackingHidBackend(_fake, ignoreFilter: true))));

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(asleep.Opens);
    }

    [Fact]
    public void A_20BC_path_of_the_same_model_wins_over_the_asleep_dongle()
    {
        var asleep = _fake.Add(0x20DD, 0x518D, 0x00FF, 0x0003, 64, 64, mi: 1);
        var wired = _fake.Add(0x20BC, 0x518C, 0x00FF, 0x0003, 64, 64, mi: 1);
        ReplyWith(wired, Reply91);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Empty(asleep.Opens);
    }

    [Fact]
    public void Other_20DD_devices_are_ignored_and_never_opened()
    {
        // BetopGame registers 20DD:<wired PID> of KP50C and others as a normal device; we never write to 20DD at all.
        var kp50c = _fake.Add(0x20DD, 0x5149, 0x00FF, 0x0003, 64, 64, mi: 1);

        Assert.Empty(Poll(Provider(new TrackingHidBackend(_fake, ignoreFilter: true))));
        Assert.Empty(Poll(Provider()));

        Assert.Empty(kp50c.Opens);
    }

    [Theory]
    [InlineData(1167)] // ERROR_DEVICE_NOT_CONNECTED: the dongle dropped 20BC as the pad fell asleep
    [InlineData(6)]    // ERROR_INVALID_HANDLE
    [InlineData(995)]  // ERROR_OPERATION_ABORTED
    public void Device_gone_mid_read_is_asleep(int win32Error)
    {
        var (vendor, _) = AddDongle();
        vendor.ReadError = new HidIoException("ReadFile failed", win32Error);
        var backend = new TrackingHidBackend(_fake);

        var reading = Assert.Single(Poll(Provider(backend)));

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Equal("Controller asleep or disconnected", reading.Detail);
        Assert.Equal(0, backend.OpenConnections);
    }

    [Fact]
    public void Device_gone_on_write_is_asleep()
    {
        var (vendor, _) = AddDongle();
        vendor.WriteError = new HidIoException("WriteFile failed", 1167);

        Assert.Equal(DeviceStatus.Asleep, Assert.Single(Poll(Provider())).Status);
    }

    [Fact]
    public void Write_timeout_is_no_answer()
    {
        var (vendor, _) = AddDongle();
        vendor.WriteError = new HidIoException("WriteFile timed out", 1460); // ERROR_TIMEOUT from the 1 s write guard

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Equal("No answer from the controller", reading.Detail);
    }

    [Fact]
    public void Removed_between_enumeration_and_open_is_asleep()
    {
        var (vendor, _) = AddDongle();
        vendor.OpenError = new HidIoException("Cannot open", 2); // ERROR_FILE_NOT_FOUND

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Equal("Controller asleep or disconnected", reading.Detail);
    }

    [Fact]
    public void Access_denied_on_open_needs_access()
    {
        var (vendor, _) = AddDongle();
        vendor.OpenError = new HidIoException("Cannot open", HidIoException.AccessDenied);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.NeedsAccess, reading.Status);
        Assert.Equal("Windows denied access to the controller", reading.Detail);
        Assert.Empty(vendor.Written);
    }

    [Fact]
    public void Other_HID_failures_are_errors()
    {
        var (vendor, _) = AddDongle();
        vendor.ReadError = new HidIoException("ReadFile failed", 1784);

        var reading = Assert.Single(Poll(Provider()));

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Could not read the controller (error 1784)", reading.Detail);
    }

    [Fact]
    public void A_refused_command_is_an_internal_error_and_is_logged()
    {
        // The collection turns into the 20DD identity after the first request: the policy refuses the retry.
        var (vendor, _) = AddDongle();
        vendor.OnWrite = _ => vendor.Info = vendor.Info with { VendorId = 0x20DD };
        var lines = new List<string>();
        void Collect(string line)
        {
            lock (lines) lines.Add(line);
        }
        Log.LineWritten += Collect;
        try
        {
            var reading = Assert.Single(Poll(Provider()));

            Assert.Equal(DeviceStatus.Error, reading.Status);
            Assert.Equal("Internal error: unsafe command refused", reading.Detail);
            Assert.Single(vendor.Written);
        }
        finally
        {
            Log.LineWritten -= Collect;
        }
        lock (lines) Assert.Contains(lines, l => l.Contains(" ERROR ") && l.Contains("Betop"));
    }

    [Fact]
    public void One_failing_model_does_not_stop_the_others()
    {
        var (kp70c2, _) = AddDongle();
        kp70c2.OpenError = new HidIoException("Cannot open", 1167);
        var kp50c = _fake.Add(0x20BC, 0x514A, 0x00FF, 0x0003, 64, 64, mi: 1);
        ReplyWith(kp50c, Reply91);

        var readings = Poll(Provider());

        Assert.Equal(DeviceStatus.Online, readings.Single(r => r.DeviceId == "betop:KP50C").Status);
        Assert.Equal(DeviceStatus.Asleep, readings.Single(r => r.DeviceId == "betop:KP70C2").Status);
    }

    [Fact]
    public void Connections_are_closed_before_the_poll_returns()
    {
        var (vendor, _) = AddDongle();
        ReplyWith(vendor, Reply91);
        var backend = new TrackingHidBackend(_fake);

        Poll(Provider(backend));

        Assert.Equal(0, backend.OpenConnections);
    }

    [Fact]
    public async Task Cancelled_poll_is_cancelled_and_sends_nothing()
    {
        var (vendor, _) = AddDongle();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().PollAsync(new CancellationToken(canceled: true)));

        Assert.Empty(vendor.Written);
    }

    [Fact]
    public async Task Concurrent_polls_are_serialized()
    {
        var (vendor, _) = AddDongle();
        var inside = 0;
        var overlapped = false;
        vendor.OnWrite = _ =>
        {
            if (Interlocked.Increment(ref inside) > 1) overlapped = true;
            Thread.Sleep(20);
            vendor.Enqueue(Reply91);
            Interlocked.Decrement(ref inside);
        };
        var provider = Provider();

        var polls = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => provider.PollAsync(CancellationToken.None))));

        Assert.False(overlapped);
        Assert.All(polls, p => Assert.Equal(DeviceStatus.Online, Assert.Single(p).Status));
    }
}
