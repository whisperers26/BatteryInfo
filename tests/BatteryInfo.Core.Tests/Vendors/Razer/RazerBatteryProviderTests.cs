using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Safety;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Razer;

namespace BatteryInfo.Core.Tests.Vendors.Razer;

public class RazerBatteryProviderTests
{
    private const string Hid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string DonglePath = @"\\?\hid#vid_1532&pid_00d7&mi_00#8&2188b82e&0&0000#" + Hid; // seen on this PC
    private const string WiredPath = @"\\?\hid#vid_1532&pid_00d6&mi_00#8&3a1c0f2&0&0000#" + Hid;

    private readonly FakeHidBackend _fake = new();
    private readonly TrackingHidBackend _backend;
    private readonly ManualTimeProvider _time = new();
    private readonly List<TimeSpan> _waits = [];

    public RazerBatteryProviderTests() => _backend = new TrackingHidBackend(_fake);

    private RazerBatteryProvider Provider() => new(_backend, _time) { Wait = (delay, _) => _waits.Add(delay) };

    // The MI_00 mouse TLC that carries the Razer report: UP 1, U 2, 91-byte feature report, report id 0.
    private FakeRazerMailbox Mailbox(ushort pid, string path, ushort usagePage = 0x0001, ushort usage = 0x0002,
        int feature = 91, int mi = 0, byte? featureReportId = 0x00, bool accessible = true) =>
        new(_fake.Add(0x1532, pid, usagePage, usage, 8, 0, feature, mi, featureReportId: featureReportId, path: path,
            accessible: accessible));

    private FakeRazerMailbox Dongle() => Mailbox(0x00D7, DonglePath);

    private FakeRazerMailbox Wired() => Mailbox(0x00D6, WiredPath);

    private IReadOnlyList<DeviceReading> Poll(RazerBatteryProvider? provider = null) =>
        (provider ?? Provider()).PollAsync(CancellationToken.None).GetAwaiter().GetResult();

    private DeviceReading PollOne() => Assert.Single(Poll());

    [Fact]
    public void Vendor_is_razer()
    {
        Assert.Equal("Razer", Provider().Vendor);
    }

    [Fact]
    public void Wired_mouse_reports_battery_and_charging()
    {
        var mouse = Wired();
        mouse.BatteryRaw = 245;
        mouse.Charging = 1;

        var reading = PollOne();

        Assert.Equal("razer:00D6", reading.DeviceId);
        Assert.Equal("Razer", reading.Vendor);
        Assert.Equal("Razer Basilisk V3 Pro 35K", reading.Name);
        Assert.Equal(DeviceKind.Mouse, reading.Kind);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(96, ChargeState.Charging)], reading.Components);
        Assert.Equal(_time.GetUtcNow(), reading.Timestamp);
        Assert.Equal("Razer 07/80", reading.Source);
        Assert.False(reading.Approximate);
        Assert.Null(reading.Detail);
    }

    [Fact]
    public void Charging_status_is_read_before_the_level_and_nothing_else_is_sent_on_the_cable()
    {
        var mouse = Wired();

        Poll();

        Assert.Equal([0x84, 0x80], mouse.SentIds);
        Assert.Equal(RazerProtocol.ChargingStatusRequest(), mouse.Device.FeaturesSet[0]);
        Assert.Equal(RazerProtocol.BatteryLevelRequest(), mouse.Device.FeaturesSet[1]);
    }

    [Fact]
    public void Collection_is_opened_with_zero_access_and_closed_before_returning()
    {
        var mouse = Wired();

        Poll();

        Assert.Equal([HidAccess.None], mouse.Device.Opens);
        Assert.Equal(0, _backend.OpenConnections);
    }

    [Fact]
    public void Each_poll_opens_a_fresh_connection()
    {
        var mouse = Wired();
        var provider = Provider();

        Poll(provider);
        Poll(provider);

        Assert.Equal(2, mouse.Device.Opens.Count);
        Assert.Equal(0, _backend.OpenConnections);
    }

    [Fact]
    public void Dongle_reading_uses_the_cable_pid_as_device_id()
    {
        var dongle = Dongle();
        dongle.BatteryRaw = 217;
        dongle.Charging = 0;

        var reading = PollOne();

        Assert.Equal("razer:00D6", reading.DeviceId);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(85, ChargeState.Discharging)], reading.Components);
    }

    [Fact]
    public void Dongle_is_asked_for_its_link_table_before_charging_and_level()
    {
        var dongle = Dongle();

        Assert.Equal(DeviceStatus.Online, PollOne().Status);

        Assert.Equal([0xBF, 0x84, 0x80], dongle.SentIds);
        Assert.Equal(RazerProtocol.WirelessStatusRequest(), dongle.Device.FeaturesSet[0]);
    }

    [Fact]
    public void Unlinked_mouse_is_asleep_and_no_07_command_is_sent()
    {
        var dongle = Dongle();
        dongle.LinkSlots = [0x02, 0x00, 0x00, 0xD7, 0x00, 0xFF, 0xFF]; // slot 1 (00D7) not linked

        var reading = PollOne();

        Assert.Equal("razer:00D6", reading.DeviceId);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Contains("dongle", reading.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([0xBF], dongle.SentIds);
    }

    [Fact]
    public void Link_table_that_does_not_list_this_mouse_does_not_decide_anything()
    {
        var dongle = Dongle();
        dongle.LinkSlots = [0x01, 0x00, 0x00, 0x99];

        Assert.Equal(DeviceStatus.Online, PollOne().Status);
        Assert.Equal([0xBF, 0x84, 0x80], dongle.SentIds);
    }

    [Fact]
    public void Unsupported_link_query_still_reads_the_battery()
    {
        var dongle = Dongle();
        dongle.Script(0xBF, Replies.NotSupported);

        Assert.Equal(DeviceStatus.Online, PollOne().Status);
        Assert.Equal([0xBF, 0x84, 0x80], dongle.SentIds);
    }

    [Fact]
    public void Unanswered_link_query_still_reads_the_battery()
    {
        var dongle = Dongle();
        dongle.ScriptEvery(0xBF, 3, Replies.NoResponse);

        Assert.Equal(DeviceStatus.Online, PollOne().Status);
        Assert.Equal(1, dongle.SentCount(0x84));
        Assert.Equal(1, dongle.SentCount(0x80));
    }

    [Fact]
    public void Cable_is_preferred_when_cable_and_dongle_are_both_present()
    {
        var dongle = Dongle();
        var wired = Wired();

        var reading = PollOne();

        Assert.Equal("razer:00D6", reading.DeviceId);
        Assert.Single(wired.Device.Opens);
        Assert.Empty(dongle.Device.Opens);
        Assert.Empty(dongle.Device.FeaturesSet);
    }

    [Fact]
    public void No_razer_device_means_no_readings_and_no_io()
    {
        var logitech = _fake.Add(0x046D, 0xC54D, 0xFF00, 0x0002, 20, 20, mi: 2);

        Assert.Empty(Poll());
        Assert.Empty(logitech.Opens);
    }

    [Fact]
    public void Unknown_razer_products_are_not_touched()
    {
        var other = Mailbox(0x0099, @"\\?\hid#vid_1532&pid_0099&mi_00#8&1&0&0000#" + Hid);

        Assert.Empty(Poll());
        Assert.Empty(other.Device.Opens);
    }

    [Fact]
    public void Only_the_plain_mi_00_mouse_collection_with_a_91_byte_feature_report_is_used()
    {
        // Razer's driver adds RZVIRTUAL children of MI_00 (Synapse got "Incorrect function" on them), and the
        // dongle has more mouse-usage collections on MI_01. All listed first so a first-match pick would fail.
        FakeRazerMailbox[] decoys =
        [
            Mailbox(0x00D7, @"\\?\hid#rzvirtual&vid_1532&pid_00d7&mi_00&col03&col01#9&1f&0&0000#" + Hid),
            Mailbox(0x00D7, @"\\?\hid#rzvirtual&vid_1532&pid_00d7&mi_00&col03&col02#9&1f&0&0001#" + Hid),
            Mailbox(0x00D7, @"\\?\hid#rzvirtual&vid_1532&pid_00d7&mi_00#9&20&0&0000#" + Hid),
            Mailbox(0x00D7, @"\\?\hid#vid_1532&pid_00d7&mi_00&col01#8&2b&0&0000#" + Hid),
            Mailbox(0x00D7, @"\\?\hid#vid_1532&pid_00d7&mi_01&col02#8&2c&0&0001#" + Hid, mi: 1),
            Mailbox(0x00D7, @"\\?\hid#vid_1532&pid_00d7&mi_00#8&2d&0&0000#" + Hid, usage: 0x0006),
            Mailbox(0x00D7, @"\\?\hid#vid_1532&pid_00d7&mi_00#8&2e&0&0000#" + Hid, usagePage: 0xFF00),
            Mailbox(0x00D7, @"\\?\hid#vid_1532&pid_00d7&mi_00#8&2f&0&0000#" + Hid, feature: 0),
            Mailbox(0x00D7, @"\\?\hid#vid_1532&pid_00d7&mi_00#8&30&0&0000#" + Hid, feature: 65),
            Mailbox(0x00D7, @"\\?\hid#vid_1532&pid_00d7&mi_00#8&31&0&0000#" + Hid, featureReportId: 0x05),
        ];
        var dongle = Dongle();

        var reading = PollOne();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Single(dongle.Device.Opens);
        Assert.All(decoys, d => Assert.Empty(d.Device.Opens));
    }

    [Fact]
    public void Rzvirtual_instance_is_excluded_even_when_its_path_looks_plain()
    {
        var virtualChild = _fake.Add(0x1532, 0x00D7, 0x0001, 0x0002, 8, 0, 91, 0, featureReportId: 0x00,
            path: DonglePath.Replace("2188b82e", "99"));
        virtualChild.Info = virtualChild.Info with { InstanceId = @"HID\RZVIRTUAL&VID_1532&PID_00D7&MI_00\9&99&0&0000" };

        Assert.Empty(Poll());
        Assert.Empty(virtualChild.Opens);
    }

    [Fact]
    public void A_collection_without_feature_report_ids_is_accepted()
    {
        var dongle = Mailbox(0x00D7, DonglePath, featureReportId: null);

        Assert.Equal(DeviceStatus.Online, PollOne().Status);
        Assert.Single(dongle.Device.Opens);
    }

    [Fact]
    public void Busy_and_new_replies_are_polled_again_without_resending()
    {
        var mouse = Wired();
        mouse.Script(0x80, Replies.New, Replies.Busy, Replies.Busy, Replies.Ok);

        var reading = PollOne();

        Assert.Equal(96, reading.Components[0].Percent);
        Assert.Equal(1, mouse.SentCount(0x80));
        Assert.Equal(1 + 4, mouse.GetFeatureCalls); // one for 07/84, four for 07/80
    }

    [Fact]
    public void Waits_after_the_send_and_between_polls()
    {
        var mouse = Wired();
        mouse.Script(0x84, Replies.Busy, Replies.Busy, Replies.Ok);

        Poll();

        TimeSpan settle = TimeSpan.FromMilliseconds(20), poll = TimeSpan.FromMilliseconds(10);
        Assert.Equal([settle, poll, poll, settle], _waits);
    }

    [Fact]
    public void Foreign_replies_in_the_shared_mailbox_are_skipped()
    {
        var mouse = Wired();
        mouse.Script(0x80,
            Replies.Foreign(0x0F, 0x80, 0x02, 0x1F, 0x00, 0x10), // same txid, other class
            Replies.Foreign(0x07, 0x84, 0x02, 0x1F, 0x00, 0x10), // same txid and class, other id
            Replies.Foreign(0x07, 0x80, 0x02, 0x1F, 0x01, 0x10), // same header, other battery id
            Replies.Foreign(0x07, 0x80, 0x03, 0x1F, 0x00, 0x10), // other size
            Replies.Foreign(0x07, 0x80, 0x02, 0x05, 0x00, 0x10), // other txid
            Replies.Ok);

        var reading = PollOne();

        Assert.Equal([BatteryComponent.Single(96, ChargeState.Charging)], reading.Components);
        Assert.Equal(1, mouse.SentCount(0x80));
    }

    [Fact]
    public void A_success_with_a_bad_crc_is_polled_again()
    {
        var mouse = Wired();
        mouse.BatteryRaw = 255;
        mouse.Script(0x80, Replies.BadCrc, Replies.Ok);

        var reading = PollOne();

        Assert.Equal(100, reading.Components[0].Percent);
        Assert.Equal(1, mouse.SentCount(0x80));
    }

    [Fact]
    public void Polling_one_send_stops_after_about_300_ms_of_polls()
    {
        var mouse = Wired();
        mouse.ScriptEvery(0x84, 3, Replies.Busy);

        Poll();

        Assert.True(mouse.GetFeatureCalls <= 3 * 30, $"{mouse.GetFeatureCalls} GetFeature calls");
    }

    [Fact]
    public void Status_04_on_every_send_reports_asleep_without_asking_for_the_level()
    {
        // The dongle answers 0x04 for 07/84 and 07/80 when the mouse is asleep, off or out of range.
        var dongle = Dongle();
        dongle.ScriptEvery(0x84, 3, Replies.NoResponse);

        var reading = PollOne();

        Assert.Equal("razer:00D6", reading.DeviceId);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Contains("asleep", reading.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, dongle.SentCount(0x84));
        Assert.Equal(0, dongle.SentCount(0x80));
    }

    [Fact]
    public void Resends_are_about_150_ms_apart()
    {
        var mouse = Wired();
        mouse.ScriptEvery(0x84, 3, Replies.NoResponse);

        Poll();

        TimeSpan settle = TimeSpan.FromMilliseconds(20), gap = TimeSpan.FromMilliseconds(150);
        Assert.Equal([settle, gap, settle, gap, settle], _waits);
    }

    [Fact]
    public void Status_04_then_success_on_a_resend_reads_the_battery()
    {
        var mouse = Wired();
        mouse.Script(0x84, Replies.NoResponse);

        var reading = PollOne();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(96, ChargeState.Charging)], reading.Components);
        Assert.Equal(2, mouse.SentCount(0x84));
    }

    [Fact]
    public void Status_04_on_the_level_after_a_good_charging_reply_reports_asleep()
    {
        var mouse = Wired();
        mouse.ScriptEvery(0x80, 3, Replies.NoResponse);

        var reading = PollOne();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Equal(3, mouse.SentCount(0x80));
    }

    [Fact]
    public void A_mailbox_that_stays_busy_is_resent_twice_then_reported_as_no_reply()
    {
        var mouse = Wired();
        mouse.ScriptEvery(0x84, 3, Replies.Busy);

        var reading = PollOne();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Equal("No valid reply from the mouse", reading.Detail);
        Assert.Equal(3, mouse.SentCount(0x84));
        Assert.Equal(3 * 30, mouse.GetFeatureCalls);
        Assert.Equal(0, mouse.SentCount(0x80));
    }

    [Fact]
    public void A_crc_that_never_checks_out_is_resent_twice_then_reported_as_no_reply()
    {
        var mouse = Wired();
        mouse.ScriptEvery(0x80, 3, Replies.BadCrc);

        var reading = PollOne();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Equal(3, mouse.SentCount(0x80));
    }

    [Fact]
    public void Unsupported_charging_status_still_reads_the_level_without_resending()
    {
        var mouse = Wired();
        mouse.Script(0x84, Replies.NotSupported);

        var reading = PollOne();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(96, ChargeState.Unknown)], reading.Components);
        Assert.Equal(1, mouse.SentCount(0x84));
    }

    [Fact]
    public void Failed_charging_status_is_resent_then_the_level_is_still_read()
    {
        var mouse = Wired();
        mouse.ScriptEvery(0x84, 3, Replies.Failure);

        var reading = PollOne();

        Assert.Equal([BatteryComponent.Single(96, ChargeState.Unknown)], reading.Components);
        Assert.Equal(3, mouse.SentCount(0x84));
    }

    [Fact]
    public void Unsupported_battery_level_is_an_error()
    {
        var mouse = Wired();
        mouse.Script(0x80, Replies.NotSupported);

        var reading = PollOne();

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Empty(reading.Components);
        Assert.NotNull(reading.Detail);
        Assert.Equal(1, mouse.SentCount(0x80));
    }

    [Fact]
    public void Failed_battery_level_is_resent_then_reported_as_an_error()
    {
        var mouse = Wired();
        mouse.ScriptEvery(0x80, 3, Replies.Failure);

        var reading = PollOne();

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal(3, mouse.SentCount(0x80));
    }

    [Fact]
    public void Full_on_external_power_reads_as_not_charging()
    {
        var mouse = Wired();
        mouse.BatteryRaw = 255;
        mouse.Charging = 5;

        Assert.Equal([BatteryComponent.Single(100, ChargeState.NotCharging)], PollOne().Components);
    }

    [Fact]
    public void Access_denied_on_open_needs_access()
    {
        // Without Razer's driver package, non-elevated opens of the mouse TLC fail with ERROR_ACCESS_DENIED.
        var dongle = Dongle();
        dongle.Device.OpenError = new HidIoException("Cannot open 1532:00D7", HidIoException.AccessDenied);

        var reading = PollOne();

        Assert.Equal("razer:00D6", reading.DeviceId);
        Assert.Equal(DeviceStatus.NeedsAccess, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Contains("access", reading.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([HidAccess.None], dongle.Device.Opens);
        Assert.Empty(dongle.Device.FeaturesSet);
    }

    [Fact]
    public void Collection_windows_could_not_open_while_listing_needs_access_without_another_open()
    {
        // What WindowsHidBackend lists when its zero-access open fails: path and ids only, no caps.
        var dongle = Mailbox(0x00D7, DonglePath, usagePage: 0, usage: 0, feature: 0, featureReportId: null, accessible: false);

        var reading = PollOne();

        Assert.Equal(DeviceStatus.NeedsAccess, reading.Status);
        Assert.Empty(dongle.Device.Opens);
    }

    [Fact]
    public void Unopenable_children_and_other_interfaces_are_not_reported()
    {
        Mailbox(0x00D7, @"\\?\hid#vid_1532&pid_00d7&mi_01&col01#8&2c&0&0000#" + Hid, 0, 0, 0, mi: 1, featureReportId: null,
            accessible: false);
        Mailbox(0x00D7, @"\\?\hid#rzvirtual&vid_1532&pid_00d7&mi_00&col03&col01#9&1f&0&0000#" + Hid, 0, 0, 0,
            featureReportId: null, accessible: false);

        Assert.Empty(Poll());
    }

    [Fact]
    public void Access_denied_on_a_feature_request_needs_access()
    {
        var mouse = Wired();
        mouse.SetFeatureError = new HidIoException("HidD_SetFeature", HidIoException.AccessDenied);

        Assert.Equal(DeviceStatus.NeedsAccess, PollOne().Status);
        Assert.Equal(0, _backend.OpenConnections);
    }

    [Theory]
    [InlineData(1167)] // ERROR_DEVICE_NOT_CONNECTED
    [InlineData(6)] // ERROR_INVALID_HANDLE
    [InlineData(995)] // ERROR_OPERATION_ABORTED
    [InlineData(31)] // ERROR_GEN_FAILURE
    [InlineData(22)] // ERROR_BAD_COMMAND
    public void Device_gone_mid_poll_ends_the_poll_quietly_as_asleep(int win32Error)
    {
        var dongle = Dongle();
        dongle.GetFeatureError = new HidIoException("HidD_GetFeature", win32Error);

        var reading = PollOne();

        Assert.Equal("razer:00D6", reading.DeviceId);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.NotNull(reading.Detail);
        Assert.Single(dongle.Device.FeaturesSet);
        Assert.Equal(0, _backend.OpenConnections);
    }

    [Fact]
    public void Device_gone_before_the_open_is_asleep()
    {
        var dongle = Dongle();
        dongle.Device.OpenError = new HidIoException("Cannot open", 1167);

        Assert.Equal(DeviceStatus.Asleep, PollOne().Status);
    }

    [Fact]
    public void Other_hid_failures_are_errors_with_the_win32_code()
    {
        var mouse = Wired();
        mouse.GetFeatureError = new HidIoException("HidD_GetFeature", 87);

        var reading = PollOne();

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Contains("87", reading.Detail);
        Assert.Equal(0, _backend.OpenConnections);
    }

    private sealed class RefuseEverything : ICommandPolicy
    {
        public bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing) => false;
    }

    [Fact]
    public void A_refused_frame_is_an_internal_error_and_nothing_is_sent()
    {
        var mouse = Wired();
        var lines = new List<string>();
        void Collect(string line)
        {
            lock (lines) lines.Add(line);
        }
        var provider = new RazerBatteryProvider(_backend, _time) { Wait = (_, _) => { }, Policy = new RefuseEverything() };

        Log.LineWritten += Collect;
        DeviceReading reading;
        try
        {
            reading = Assert.Single(Poll(provider));
        }
        finally
        {
            Log.LineWritten -= Collect;
        }

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Internal error: unsafe command refused", reading.Detail);
        Assert.Empty(mouse.Device.FeaturesSet);
        Assert.Equal(0, _backend.OpenConnections);
        lock (lines) Assert.Contains(lines, l => l.Contains(" ERROR ") && l.Contains("Razer"));
    }

    [Fact]
    public async Task A_cancelled_poll_throws_and_sends_nothing()
    {
        var mouse = Wired();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().PollAsync(cts.Token));

        Assert.Empty(mouse.Device.FeaturesSet);
    }

    [Fact]
    public async Task Cancellation_during_an_exchange_stops_the_poll_and_closes_the_connection()
    {
        var mouse = Wired();
        using var cts = new CancellationTokenSource();
        var provider = new RazerBatteryProvider(_backend, _time) { Wait = (_, _) => cts.Cancel() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.PollAsync(cts.Token));

        Assert.Single(mouse.Device.FeaturesSet);
        Assert.Equal(0, _backend.OpenConnections);
    }

    [Fact]
    public void Default_wait_really_pauses_after_each_send()
    {
        Wired();
        var provider = new RazerBatteryProvider(_backend, _time);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var reading = Assert.Single(Poll(provider));

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(35), $"took {clock.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public async Task Cancelling_a_busy_poll_stops_it_promptly()
    {
        // A mailbox that stays busy would keep the poll going for about 1.2 s. The test cancels right after the
        // first request goes out, from its own thread: a timer-based cancel runs on the thread pool, which the
        // parallel tests can starve until the whole busy poll has finished.
        var mouse = Wired();
        mouse.ScriptEvery(0x84, 3, Replies.Busy);
        using var firstSend = new ManualResetEventSlim();
        var setFeature = mouse.Device.OnSetFeature!;
        mouse.Device.OnSetFeature = frame =>
        {
            setFeature(frame);
            firstSend.Set();
        };
        var provider = new RazerBatteryProvider(_backend, _time);
        using var cts = new CancellationTokenSource();
        var poll = Task.Factory.StartNew(() => provider.PollAsync(cts.Token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

        Assert.True(firstSend.Wait(TimeSpan.FromSeconds(5)), "the poll never sent a request");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll);
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(800), $"took {clock.Elapsed.TotalMilliseconds} ms after the cancel");
        Assert.Equal(0, _backend.OpenConnections);
    }

    [Fact]
    public async Task Concurrent_polls_are_serialized()
    {
        Wired();
        var provider = new RazerBatteryProvider(_backend, _time) { Wait = (_, _) => Thread.Sleep(1) };

        var polls = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => provider.PollAsync(CancellationToken.None))));

        Assert.All(polls, p => Assert.Equal(DeviceStatus.Online, Assert.Single(p).Status));
        Assert.Equal(1, _backend.MaxConcurrentConnections);
    }
}
