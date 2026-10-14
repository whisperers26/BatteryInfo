using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Logitech;

namespace BatteryInfo.Core.Tests.Vendors.Logitech;

public class LogitechBatteryProviderTests
{
    private static readonly HidppTimings FastTimings = new(
        Register: TimeSpan.FromMilliseconds(40),
        Ping: TimeSpan.FromMilliseconds(40),
        Feature: TimeSpan.FromMilliseconds(40),
        Slice: TimeSpan.FromMilliseconds(1),
        BusyRetryDelay: TimeSpan.FromMilliseconds(1));

    private readonly FakeHidBackend _backend = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakeHidppReceiver _receiver;
    private readonly LogitechBatteryProvider _provider;

    public LogitechBatteryProviderTests()
    {
        _receiver = new FakeHidppReceiver(_backend);
        _provider = new LogitechBatteryProvider(_backend, _time, FastTimings);
    }

    private async Task<DeviceReading> PollSingle()
    {
        var readings = await _provider.PollAsync(CancellationToken.None);
        return Assert.Single(readings);
    }

    private static byte[] Long(string hex)
    {
        var report = new byte[20];
        Convert.FromHexString(hex.Replace(" ", "")).CopyTo(report, 0);
        return report;
    }

    [Fact]
    public async Task Online_mouse_in_slot_1_reads_the_unified_battery()
    {
        _receiver.Pair(1);

        var reading = await PollSingle();

        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal("Logitech", reading.Vendor);
        Assert.Equal("PRO X2 SUPERSTRIKE", reading.Name);
        Assert.Equal(DeviceKind.Mouse, reading.Kind);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(56, ChargeState.Discharging)], reading.Components);
        Assert.Equal(_time.GetUtcNow(), reading.Timestamp);
        Assert.Equal("HID++ 0x1004", reading.Source);
        Assert.False(reading.Approximate);
        Assert.Null(reading.Detail);
    }

    [Fact]
    public async Task Every_request_is_a_short_frame_on_the_short_collection_with_software_id_0x0C()
    {
        _receiver.Pair(1);

        await PollSingle();

        Assert.Empty(_receiver.Long.Written);
        Assert.All(_receiver.Requests, frame => Assert.Equal(7, frame.Length));
        Assert.All(_receiver.Requests.Where(f => f[2] < 0x80), frame => Assert.Equal(0x0C, frame[3] & 0x0F));
    }

    [Fact]
    public async Task Collections_are_opened_shared_read_write_and_closed_after_the_poll()
    {
        _receiver.Pair(1);

        await PollSingle();

        Assert.Equal([HidAccess.ReadWrite], _receiver.Short.Opens);
        Assert.Equal([HidAccess.ReadWrite], _receiver.Long.Opens);
    }

    [Fact]
    public async Task Empty_slot_is_skipped_without_a_ping()
    {
        _receiver.Pair(1);

        await PollSingle();

        Assert.DoesNotContain(_receiver.Requests, f => f[1] == 0x02);
    }

    [Fact]
    public async Task Max_devices_outside_1_to_6_scans_all_six_slots()
    {
        _receiver.MaxDevices = 0;
        _receiver.Pair(5);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Contains(_receiver.Requests, f => f[2] == 0x83 && f[4] == 0x25);
    }

    [Fact]
    public async Task Asleep_mouse_is_reported_asleep_with_its_identity_from_the_receiver()
    {
        _receiver.Pair(1).Link = FakeLink.Asleep;

        var reading = await PollSingle();

        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal("PRO X2 SUPERSTRIKE", reading.Name);
        Assert.Equal(DeviceKind.Mouse, reading.Kind);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.NotNull(reading.Detail);
    }

    [Fact]
    public async Task Paired_slot_whose_ping_reports_unknown_device_is_asleep()
    {
        // Live on 2026-10-14 13:39 with the mouse off: B5/20 lists slot 1, the ping gets 10 01 8F 00 1C 08 00.
        _receiver.Pair(1).Link = FakeLink.Disconnected;

        var reading = await PollSingle();

        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal("PRO X2 SUPERSTRIKE", reading.Name);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
    }

    [Fact]
    public async Task Device_switched_off_after_a_good_poll_keeps_its_cached_identity()
    {
        var device = _receiver.Pair(1);
        device.Wpid = 0x4099;
        device.Name = "G502 X PLUS";
        await PollSingle();
        device.Link = FakeLink.Disconnected;

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Equal("G502 X PLUS", reading.Name);
    }

    [Fact]
    public async Task Asleep_device_of_an_unknown_model_is_named_by_its_codename()
    {
        var device = _receiver.Pair(1);
        device.Link = FakeLink.Asleep;
        device.Wpid = 0x4099;
        device.Codename = "G502 X";

        var reading = await PollSingle();

        Assert.Equal("G502 X", reading.Name);
        Assert.Equal(DeviceKind.Mouse, reading.Kind);
    }

    [Fact]
    public async Task Ping_timeout_means_asleep()
    {
        _receiver.Pair(1).Link = FakeLink.Silent;

        Assert.Equal(DeviceStatus.Asleep, (await PollSingle()).Status);
    }

    [Fact]
    public async Task Device_that_stops_answering_after_the_ping_is_asleep()
    {
        _receiver.Pair(1).SilentAfterPing = true;

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Equal("logitech:B4655832", reading.DeviceId);
    }

    [Fact]
    public async Task Ping_is_iroot_fn1_with_a_nonzero_marker()
    {
        _receiver.Pair(1).Link = FakeLink.Asleep;

        await PollSingle();

        var ping = _receiver.Requests.Single(f => f[1] == 0x01 && f[2] == 0x00 && f[3] == 0x1C);
        Assert.Equal(0, ping[4]);
        Assert.NotEqual(0, ping[6]);
    }

    [Fact]
    public async Task Foreign_replies_events_and_notifications_are_ignored()
    {
        _receiver.Pair(1);
        _receiver.Noise = frame =>
        [
            // G HUB (swId 0x0D) asking the same thing and getting a different answer.
            (true, [0x11, frame[1], frame[2], (byte)(frame[3] & 0xF0 | 0x0D), 0x10, 0x01, 0x02, 0x03, .. new byte[12]]),
            // lamparray-style reply with another software id but the same marker.
            (true, [0x11, frame[1], frame[2], (byte)(frame[3] & 0xF0 | 0x0A), frame[4], frame[5], frame[6], .. new byte[13]]),
            // a battery event of another device.
            (true, Long("11 02 06 00 05 08 00 00")),
            // a receiver notification on the short collection.
            (false, [0x10, 0x02, 0x41, 0x0C, 0x42, 0x99, 0x40]),
        ];

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal(56, reading.Components[0].Percent);
        Assert.Equal("PRO X2 SUPERSTRIKE", reading.Name);
    }

    [Fact]
    public async Task Receiver_unplugged_before_the_first_read_ends_the_poll_quietly()
    {
        _receiver.Pair(1);
        _receiver.Long.ReadError = new HidIoException("ReadFile", 1167);

        var readings = await _provider.PollAsync(CancellationToken.None);

        Assert.Empty(readings);
    }

    [Fact]
    public async Task Receiver_unplugged_mid_read_reports_the_device_asleep_without_throwing()
    {
        _receiver.Pair(1);
        _receiver.Pair(2, new FakeHidppDevice { UnitId = 0x11223344 });
        _receiver.Noise = frame =>
        {
            if (frame[1] == 0x01 && frame[2] == 0x00 && frame[3] >> 4 == 1)
                _receiver.Long.ReadError = new HidIoException("ReadFile", 1167);
            return [];
        };

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.DoesNotContain(_receiver.Requests, f => f[1] == 0x02 || f[2] == 0x83 && f[4] == 0x21);
    }

    [Fact]
    public async Task Access_denied_reports_the_receiver_as_needing_access()
    {
        _receiver.Pair(1);
        _receiver.Short.OpenError = new HidIoException("CreateFile", 5);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.NeedsAccess, reading.Status);
        Assert.Equal("logitech:usb-C54D", reading.DeviceId);
        Assert.NotNull(reading.Detail);
        Assert.Empty(_receiver.Requests);
    }

    [Fact]
    public async Task Long_collection_access_denied_reports_needs_access()
    {
        _receiver.Pair(1);
        _receiver.Long.OpenError = new HidIoException("CreateFile", 5);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.NeedsAccess, reading.Status);
    }

    [Fact]
    public async Task Receiver_gone_at_open_gives_no_reading()
    {
        _receiver.Short.OpenError = new HidIoException("CreateFile", 1167);

        Assert.Empty(await _provider.PollAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Other_open_failures_are_errors()
    {
        _receiver.Short.OpenError = new HidIoException("CreateFile", 32);

        Assert.Equal(DeviceStatus.Error, (await PollSingle()).Status);
    }

    [Fact]
    public async Task Cancelled_poll_throws_operation_cancelled()
    {
        _receiver.Pair(1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _provider.PollAsync(new CancellationToken(canceled: true)));
        Assert.Empty(_receiver.Requests);
    }

    [Fact]
    public async Task Cancellation_mid_poll_stops_before_the_next_step()
    {
        _receiver.Pair(1);
        _receiver.Pair(2, new FakeHidppDevice { UnitId = 0x22222222 });
        using var cancellation = new CancellationTokenSource();
        _receiver.Noise = frame =>
        {
            if (frame[1] == 0x01 && frame[2] == 0x00 && frame[3] >> 4 == 1) cancellation.Cancel();
            return [];
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _provider.PollAsync(cancellation.Token));

        Assert.DoesNotContain(_receiver.Requests, f => f[1] == 0x02 || f[2] == 0x83 && f[4] == 0x21);
        Assert.Equal(1, _receiver.Requests.Count(f => f[1] == 0x01 && f[2] < 0x80));
    }

    [Fact]
    public async Task Battery_status_feature_is_used_when_unified_battery_is_absent()
    {
        var device = _receiver.Pair(1);
        device.Features.Remove(0x1004);
        device.Features[0x1000] = 0x07;

        var reading = await PollSingle();

        Assert.Equal("HID++ 0x1000", reading.Source);
        Assert.Equal([BatteryComponent.Single(50, ChargeState.Discharging)], reading.Components);
        Assert.False(reading.Approximate);
    }

    [Fact]
    public async Task Battery_voltage_feature_is_approximate()
    {
        var device = _receiver.Pair(1);
        device.Features.Remove(0x1004);
        device.Features[0x1001] = 0x07;

        var reading = await PollSingle();

        Assert.Equal("HID++ 0x1001", reading.Source);
        Assert.Equal(81, reading.Components[0].Percent);
        Assert.True(reading.Approximate);
    }

    [Fact]
    public async Task Adc_measurement_feature_is_read_and_inactive_means_asleep()
    {
        var device = _receiver.Pair(1);
        device.Features.Remove(0x1004);
        device.Features[0x1F20] = 0x07;

        var active = await PollSingle();
        device.AdcMeasurement = [0x0F, 0x3C, 0x00];
        var inactive = await PollSingle();

        Assert.Equal("HID++ 0x1F20", active.Source);
        Assert.Equal(87, active.Components[0].Percent);
        Assert.Equal(DeviceStatus.Asleep, inactive.Status);
    }

    [Fact]
    public async Task Device_without_a_battery_feature_is_online_with_no_value()
    {
        _receiver.Pair(1).Features.Remove(0x1004);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Empty(reading.Components);
        Assert.NotNull(reading.Detail);
    }

    [Fact]
    public async Task Without_the_name_feature_the_model_table_names_the_device()
    {
        _receiver.Pair(1).Features.Remove(0x0005);

        Assert.Equal("PRO X2 SUPERSTRIKE", (await PollSingle()).Name);
    }

    [Fact]
    public async Task Busy_device_is_retried()
    {
        var device = _receiver.Pair(1);
        device.NextErrors.Enqueue(0x08);
        device.NextErrors.Enqueue(0x08);

        var reading = await PollSingle();

        Assert.Equal(56, reading.Components[0].Percent);
    }

    [Fact]
    public async Task Hidpp20_error_on_the_battery_read_is_an_error_reading()
    {
        var device = _receiver.Pair(1);
        _receiver.Noise = frame =>
        {
            if (frame[2] == 0x06 && frame[3] >> 4 == 1) device.NextErrors.Enqueue(0x05);
            return [];
        };

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Contains("05", reading.Detail);
    }

    [Fact]
    public async Task Two_paired_devices_give_two_readings()
    {
        _receiver.Pair(1);
        _receiver.Pair(2, new FakeHidppDevice { UnitId = 0x0BADF00D, Name = "G915 X", DeviceType = 0, UnifiedStatus = [0x14, 0x02, 0x01, 0x01] });

        var readings = await _provider.PollAsync(CancellationToken.None);

        Assert.Equal(2, readings.Count);
        var keyboard = readings.Single(r => r.DeviceId == "logitech:0BADF00D");
        Assert.Equal(DeviceKind.Keyboard, keyboard.Kind);
        Assert.Equal([BatteryComponent.Single(20, ChargeState.Charging)], keyboard.Components);
    }

    // Feature-index confirmation. A getFeature reply does not name the feature it answers: only the device index, index
    // 0, fn0 and the software id tie it to a request. Another program on the receiver that happens to use software id
    // 0x0C (logi_lamparray_service picks its own at runtime) can put its reply in front of ours.

    // §11: the read functions this app may call, by feature id.
    private static readonly Dictionary<ushort, int[]> ReadFunctions = new()
    {
        [0x0001] = [0, 1], [0x0003] = [0, 1], [0x0005] = [0, 1, 2], [0x0007] = [0, 1],
        [0x1000] = [0, 1], [0x1001] = [0], [0x1004] = [0, 1], [0x1F20] = [0],
    };

    private void AssertEveryFeatureCallIsAReadOfTheFeatureReallyAtThatIndex(FakeHidppDevice device)
    {
        foreach (var frame in _receiver.Requests.Where(f => f[1] == 0x01 && f[2] is > 0x00 and < 0x80))
        {
            var feature = device.Features.Where(f => f.Value == frame[2]).Select(f => (ushort?)f.Key).FirstOrDefault();
            Assert.True(feature is { } id && ReadFunctions.GetValueOrDefault(id, []).Contains(frame[3] >> 4),
                $"{Convert.ToHexString(frame)} calls fn{frame[3] >> 4} of feature {feature:X4} at index {frame[2]:X2}");
        }
    }

    private void InjectStrayGetFeatureReplyBefore(ushort featureId, byte strayIndex)
    {
        var request = HidppProtocol.GetFeature(0x01, featureId, LogitechBatteryProvider.SoftwareId);
        var injected = false;
        _receiver.Noise = frame =>
        {
            if (injected || !frame.SequenceEqual(request)) return [];
            injected = true;
            return [(true, Long($"11 01 00 0C {strayIndex:X2} 00 01"))];
        };
    }

    [Theory]
    [InlineData(HidppFeature.FeatureSet, 0x0A)]
    [InlineData(HidppFeature.DeviceFwInfo, 0x0A)]
    [InlineData(HidppFeature.DeviceName, 0x06)] // would make 0x1004 fn2 (a set function) the device-type read
    [InlineData(HidppFeature.WirelessDeviceStatus, 0x0A)]
    [InlineData(HidppFeature.UnifiedBattery, 0x0A)]
    public async Task Stray_get_feature_reply_is_caught_before_any_call_to_the_index_it_names(ushort featureId, byte strayIndex)
    {
        var device = _receiver.Pair(1);
        device.Features[0x8071] = 0x0A; // RGB_EFFECTS: none of its functions is on the allow-list
        InjectStrayGetFeatureReplyBefore(featureId, strayIndex);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("logitech:B4655832", reading.DeviceId);
        AssertEveryFeatureCallIsAReadOfTheFeatureReallyAtThatIndex(device);
    }

    [Fact]
    public async Task After_a_stray_get_feature_reply_the_next_poll_resolves_from_scratch()
    {
        var device = _receiver.Pair(1);
        device.Features[0x8071] = 0x0A;
        InjectStrayGetFeatureReplyBefore(HidppFeature.DeviceFwInfo, 0x0A);
        await PollSingle();
        _receiver.Noise = null;
        var lookups = _receiver.FeatureLookups;

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal(56, reading.Components[0].Percent);
        Assert.True(_receiver.FeatureLookups - lookups > 2);
        AssertEveryFeatureCallIsAReadOfTheFeatureReallyAtThatIndex(device);
    }

    [Fact]
    public async Task Every_feature_index_is_confirmed_with_get_feature_id_before_it_is_called()
    {
        _receiver.Pair(1);

        await PollSingle();

        var requests = _receiver.Requests.ToList();
        foreach (var (position, frame) in requests.Index().Where(r => r.Item[1] == 0x01 && r.Item[2] is > 0x01 and < 0x80))
        {
            var confirmation = HidppProtocol.GetFeatureId(0x01, 0x01, frame[2], LogitechBatteryProvider.SoftwareId);
            Assert.Contains(requests.Take(position), f => f.SequenceEqual(confirmation));
        }
    }

    [Fact]
    public async Task Device_without_feature_set_at_index_1_is_an_error_and_gets_no_feature_calls()
    {
        _receiver.Pair(1).MoveFeature(HidppFeature.FeatureSet, 0x09);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.DoesNotContain(_receiver.Requests, f => f[1] == 0x01 && f[2] is > 0x00 and < 0x80);
    }

    // Feature-index cache

    [Fact]
    public async Task Second_poll_reuses_resolved_features_and_only_re_checks_the_battery_index()
    {
        _receiver.Pair(1);
        await PollSingle();
        var lookups = _receiver.FeatureLookups;
        var nameCalls = _receiver.CallsTo(0x01, 0x03, 0) + _receiver.CallsTo(0x01, 0x03, 1) + _receiver.CallsTo(0x01, 0x03, 2);
        var infoCalls = _receiver.CallsTo(0x01, 0x02, 0);
        var capabilityCalls = _receiver.CallsTo(0x01, 0x06, 0);

        var reading = await PollSingle();

        Assert.Equal(lookups + 1, _receiver.FeatureLookups);
        Assert.Contains(_receiver.Requests.TakeLast(3), f => f.SequenceEqual(new byte[] { 0x10, 0x01, 0x00, 0x0C, 0x10, 0x04, 0x00 }));
        Assert.Equal(nameCalls, _receiver.CallsTo(0x01, 0x03, 0) + _receiver.CallsTo(0x01, 0x03, 1) + _receiver.CallsTo(0x01, 0x03, 2));
        Assert.Equal(infoCalls, _receiver.CallsTo(0x01, 0x02, 0));
        Assert.Equal(capabilityCalls, _receiver.CallsTo(0x01, 0x06, 0));
        Assert.Equal("PRO X2 SUPERSTRIKE", reading.Name);
        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal(56, reading.Components[0].Percent);
    }

    [Fact]
    public async Task Device_that_stops_answering_mid_resolution_is_resolved_again_next_time()
    {
        var device = _receiver.Pair(1);
        _receiver.Noise = frame =>
        {
            if (frame[2] == 0x03 && frame[3] >> 4 == 0) device.Link = FakeLink.Silent;
            return [];
        };
        var first = await PollSingle();
        _receiver.Noise = null;
        device.Link = FakeLink.Online;
        var lookups = _receiver.FeatureLookups;

        var second = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, first.Status);
        Assert.True(_receiver.FeatureLookups - lookups > 2);
        Assert.Equal("PRO X2 SUPERSTRIKE", second.Name);
        Assert.Equal(56, second.Components[0].Percent);
    }

    [Fact]
    public async Task Moved_battery_index_is_detected_before_any_call_to_it()
    {
        var device = _receiver.Pair(1);
        await PollSingle();
        device.MoveFeature(0x1004, 0x07);
        var oldIndexCalls = _receiver.CallsTo(0x01, 0x06, 1);

        var reading = await PollSingle();

        Assert.Equal(56, reading.Components[0].Percent);
        Assert.Equal(oldIndexCalls, _receiver.CallsTo(0x01, 0x06, 1));
        Assert.Equal(1, _receiver.CallsTo(0x01, 0x07, 1));
    }

    [Fact]
    public async Task Invalid_feature_index_error_re_resolves_and_retries_once()
    {
        var device = _receiver.Pair(1);
        await PollSingle();
        var lookups = _receiver.FeatureLookups;
        device.NextErrors.Enqueue(0x06);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal(56, reading.Components[0].Percent);
        Assert.True(_receiver.FeatureLookups - lookups > 2);
    }

    [Fact]
    public async Task Repeated_invalid_feature_index_is_an_error_and_re_resolves_next_time()
    {
        var device = _receiver.Pair(1);
        await PollSingle();
        device.NextErrors.Enqueue(0x06);
        device.NextErrors.Enqueue(0x06);

        var failed = await PollSingle();
        var lookups = _receiver.FeatureLookups;
        var recovered = await PollSingle();

        Assert.Equal(DeviceStatus.Error, failed.Status);
        Assert.Equal("logitech:B4655832", failed.DeviceId);
        Assert.Equal(DeviceStatus.Online, recovered.Status);
        Assert.True(_receiver.FeatureLookups - lookups > 2);
    }

    [Theory]
    [InlineData(true, "11 01 04 00 01 01 01")]  // 0x1D4B event: reconnected
    [InlineData(false, "10 01 41 0C 02 BD 40")] // receiver 0x41: link established
    public async Task Reconnect_seen_during_a_poll_makes_the_next_poll_re_resolve(bool onLong, string hex)
    {
        _receiver.Pair(1);
        await PollSingle();
        var report = Long(hex)[..(onLong ? 20 : 7)];
        _receiver.Noise = frame => frame[2] == 0x00 && frame[3] >> 4 == 1 ? [(onLong, report)] : [];
        await PollSingle();
        _receiver.Noise = null;
        var lookups = _receiver.FeatureLookups;

        var reading = await PollSingle();

        Assert.Equal(56, reading.Components[0].Percent);
        Assert.True(_receiver.FeatureLookups - lookups > 2);
    }

    [Fact]
    public async Task Reconnect_event_with_another_status_is_ignored()
    {
        _receiver.Pair(1);
        await PollSingle();
        _receiver.Noise = frame => frame[2] == 0x00 && frame[3] >> 4 == 1 ? [(true, Long("11 01 04 00 00 00 00"))] : [];
        await PollSingle();
        var lookups = _receiver.FeatureLookups;

        await PollSingle();

        Assert.Equal(lookups + 1, _receiver.FeatureLookups);
    }

    [Fact]
    public async Task Another_device_in_the_slot_is_resolved_from_scratch()
    {
        _receiver.Pair(1);
        await PollSingle();
        _receiver.Pair(1, new FakeHidppDevice { UnitId = 0x11111111, Wpid = 0x4074, Name = "G305", UnifiedStatus = [0x50, 0x04, 0x00, 0x00] });

        var reading = await PollSingle();

        Assert.Equal("logitech:11111111", reading.DeviceId);
        Assert.Equal("G305", reading.Name);
        Assert.Equal(80, reading.Components[0].Percent);
    }

    [Fact]
    public async Task Asleep_after_online_keeps_the_name_learnt_from_the_device()
    {
        var device = _receiver.Pair(1);
        device.Wpid = 0x4099;
        device.Name = "G502 X PLUS";
        device.Codename = "G502 X";
        await PollSingle();
        device.Link = FakeLink.Asleep;

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Equal("G502 X PLUS", reading.Name);
        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.DoesNotContain(_receiver.Requests, f => f[2] == 0x83 && f[4] == 0x40);
    }

    [Fact]
    public async Task Replugged_receiver_is_resolved_from_scratch()
    {
        _receiver.Pair(1);
        await PollSingle();
        _backend.Devices.Remove(_receiver.Short);
        _backend.Devices.Remove(_receiver.Long);
        Assert.Empty(await _provider.PollAsync(CancellationToken.None));
        _backend.Devices.Add(_receiver.Short);
        _backend.Devices.Add(_receiver.Long);
        var lookups = _receiver.FeatureLookups;

        await PollSingle();

        Assert.True(_receiver.FeatureLookups - lookups > 2);
    }

    [Fact]
    public async Task Access_denied_after_a_good_poll_reports_the_known_devices()
    {
        _receiver.Pair(1);
        await PollSingle();
        _receiver.Short.OpenError = new HidIoException("CreateFile", 5);

        var reading = await PollSingle();

        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal("PRO X2 SUPERSTRIKE", reading.Name);
        Assert.Equal(DeviceStatus.NeedsAccess, reading.Status);
    }

    [Fact]
    public async Task Hung_receiver_reports_the_known_devices_asleep()
    {
        _receiver.Pair(1);
        await PollSingle();
        _receiver.Silent = true;

        var reading = await PollSingle();

        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
    }

    [Fact]
    public async Task Unpaired_slot_is_forgotten()
    {
        _receiver.Pair(1);
        await PollSingle();
        _receiver.Slots.Clear();

        Assert.Empty(await _provider.PollAsync(CancellationToken.None));
        _receiver.Silent = true;
        Assert.Empty(await _provider.PollAsync(CancellationToken.None));
    }

    // On its own cable (046D:C0A8, device index 0xFF)

    private FakeHidppDevice PlugCable(FakeHidppDevice? device = null)
    {
        var cable = new FakeHidppReceiver(_backend, 0xC0A8, @"USB\VID_046D&PID_C0A8&MI_01\7&2A6B2F3&0&0001", wired: true);
        return cable.Pair(0xFF, device ?? new FakeHidppDevice { UnifiedStatus = [0x3A, 0x04, 0x01, 0x01] });
    }

    [Fact]
    public async Task Wired_mouse_is_read_at_device_index_0xFF_without_receiver_registers()
    {
        PlugCable();

        var reading = await PollSingle();

        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal("PRO X2 SUPERSTRIKE", reading.Name);
        Assert.Equal([BatteryComponent.Single(58, ChargeState.Charging)], reading.Components);
        var cable = _backend.Devices.Single(d => d.Info.ProductId == 0xC0A8 && d.Info.Usage == 0x0001);
        Assert.All(cable.Written, f => Assert.Equal(0xFF, f[1]));
        Assert.DoesNotContain(cable.Written, f => f[2] >= 0x80);
    }

    [Fact]
    public async Task Mouse_on_cable_and_asleep_in_its_receiver_slot_is_one_online_reading()
    {
        _receiver.Pair(1).Link = FakeLink.Asleep;
        PlugCable();

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal(ChargeState.Charging, reading.Components[0].Charge);
    }

    [Fact]
    public async Task Mouse_answering_on_both_paths_is_one_reading()
    {
        _receiver.Pair(1);
        PlugCable();

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
    }

    [Fact]
    public async Task Different_devices_on_cable_and_receiver_are_both_reported()
    {
        _receiver.Pair(1);
        PlugCable(new FakeHidppDevice { UnitId = 0x22222222 });

        Assert.Equal(2, (await _provider.PollAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Silent_wired_device_never_seen_before_gives_no_reading()
    {
        PlugCable().Link = FakeLink.Silent;

        Assert.Empty(await _provider.PollAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Silent_wired_device_seen_before_is_asleep()
    {
        var device = PlugCable();
        await PollSingle();
        device.Link = FakeLink.Silent;

        var reading = await PollSingle();

        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
    }

    // HID++ 1.0 devices (ping answered with INVALID_SUB_ID)

    private FakeHidppDevice PairHidpp10() =>
        _receiver.Pair(1, new FakeHidppDevice { Link = FakeLink.Hidpp10, Wpid = 0x1025, Codename = "M705" });

    [Fact]
    public async Task Hidpp10_device_reads_battery_register_0x0D()
    {
        PairHidpp10();

        var reading = await PollSingle();

        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal("M705", reading.Name);
        Assert.Equal(DeviceKind.Mouse, reading.Kind);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(75, ChargeState.Discharging)], reading.Components);
        Assert.Equal("HID++ 1.0 0x0D", reading.Source);
        Assert.False(reading.Approximate);
        Assert.DoesNotContain(_receiver.Requests, f => f[1] == 0x01 && f[2] == 0x00 && f[3] >> 4 == 0);
    }

    [Fact]
    public async Task Hidpp10_device_without_register_0x0D_reads_register_0x07()
    {
        PairHidpp10().Register0D = null;

        var reading = await PollSingle();

        Assert.Equal("HID++ 1.0 0x07", reading.Source);
        Assert.Equal([BatteryComponent.Single(50, ChargeState.Discharging)], reading.Components);
        Assert.True(reading.Approximate);
    }

    [Fact]
    public async Task Hidpp10_device_without_battery_registers_is_online_with_no_value()
    {
        var device = PairHidpp10();
        device.Register0D = null;
        device.Register07 = null;

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Empty(reading.Components);
    }

    [Theory]
    [InlineData(0x09)] // RESOURCE_ERROR
    [InlineData(0x04)] // CONNECT_FAIL
    [InlineData(0x08)] // UNKNOWN_DEVICE
    public async Task Hidpp10_device_the_receiver_cannot_reach_after_the_ping_is_asleep(byte error)
    {
        // Ping answered 10 01 8F 00 1C 01 00 (online), then e.g. 10 01 8F 81 0D 09 00 and 10 01 8F 81 07 09 00.
        var device = PairHidpp10();
        device.RegisterFailures[0x0D] = error;
        device.RegisterFailures[0x07] = error;

        var reading = await PollSingle();

        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
    }

    [Fact]
    public async Task Hidpp10_device_that_stops_answering_after_a_refused_register_is_asleep()
    {
        var device = PairHidpp10();
        device.Register0D = null;             // refused: 0x02 INVALID_ADDRESS
        device.RegisterFailures[0x07] = null; // no answer

        Assert.Equal(DeviceStatus.Asleep, (await PollSingle()).Status);
    }

    [Fact]
    public async Task Other_vendors_devices_are_never_opened()
    {
        var razer = _backend.Add(0x1532, 0x00D7, 0xFF00, 0x0001, 7, 7);
        var bolt = _backend.Add(0x046D, 0xC548, 0xFF00, 0x0001, 7, 7);
        _receiver.Pair(1);

        await PollSingle();

        Assert.Empty(razer.Opens);
        Assert.Empty(bolt.Opens);
    }
}
