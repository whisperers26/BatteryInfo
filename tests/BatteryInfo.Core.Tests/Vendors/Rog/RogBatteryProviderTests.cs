using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Safety;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Rog;
using static BatteryInfo.Core.Tests.Vendors.Rog.FakeOmni;

namespace BatteryInfo.Core.Tests.Vendors.Rog;

public class RogBatteryProviderTests
{
    private const string AzothXId = "rog:azoth-x:T4MPKRD01THC";
    private const string MouseId = "rog:1ab0:T4MPKRD01THC";

    private readonly FakeHidBackend _backend = new();
    private readonly ManualTimeProvider _time = new();

    private RogBatteryProvider Provider() => new(_backend, _time);

    private Task<IReadOnlyList<DeviceReading>> Poll() => Provider().PollAsync(CancellationToken.None);

    private async Task<DeviceReading> PollSingle() => Assert.Single(await Poll());

    // --- OMNI receiver with the Azoth X (the hardware on this PC) ---

    [Fact]
    public async Task Omni_keyboard_reads_45_percent_discharging()
    {
        _ = new FakeOmni(_backend);

        var reading = await PollSingle();

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal("ROG", reading.Vendor);
        Assert.Equal("ROG Azoth X", reading.Name);
        Assert.Equal(DeviceKind.Keyboard, reading.Kind);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(45, ChargeState.Discharging)], reading.Components);
        Assert.Equal(_time.GetUtcNow(), reading.Timestamp);
        Assert.Equal("ROG 12 01", reading.Source);
        Assert.False(reading.Approximate);
        Assert.Null(reading.Detail);
    }

    [Fact]
    public void Vendor_is_ROG()
    {
        Assert.Equal("ROG", Provider().Vendor);
    }

    [Fact]
    public async Task Omni_poll_sends_scan_then_keyboard_connection_and_power_as_full_64_byte_reports()
    {
        var omni = new FakeOmni(_backend);

        await Poll();

        Assert.Equal([RogProtocol.ScanForPairedDevices], omni.Receiver.Commands);
        Assert.Equal([RogProtocol.KeyboardConnectionType, RogProtocol.KeyboardPowerInfo], omni.KeyboardSlot.Commands);
        Assert.Empty(omni.MouseSlot.Device.Written);
        Assert.All(omni.Receiver.Device.Written.Concat(omni.KeyboardSlot.Device.Written), frame =>
        {
            Assert.Equal(64, frame.Length);
            Assert.All(frame[5..], b => Assert.Equal(0, b));
        });
        Assert.All(omni.Receiver.Device.Written, frame => Assert.Equal(0x01, frame[0]));
        Assert.All(omni.KeyboardSlot.Device.Written, frame => Assert.Equal(0x02, frame[0]));
    }

    [Fact]
    public async Task Event_and_other_collections_are_never_opened()
    {
        var omni = new FakeOmni(_backend);

        await Poll();

        Assert.Empty(omni.DongleEvents.Opens);
        Assert.Empty(omni.KeyboardEvents.Opens);
        Assert.Empty(omni.MouseEvents.Opens);
        Assert.Empty(omni.BootKeyboard.Opens);
        Assert.Equal([HidAccess.ReadWrite], omni.Receiver.Device.Opens);
        Assert.Equal([HidAccess.ReadWrite], omni.KeyboardSlot.Device.Opens);
    }

    [Fact]
    public async Task Unknown_asus_devices_are_never_opened()
    {
        _ = new FakeOmni(_backend);
        var display = _backend.Add(0x0B05, 0x1BCA, 0xFF72, 0x00A1, 65, 65, mi: 1, outputReportId: 0xEC);

        await Poll();

        Assert.Empty(display.Opens);
    }

    [Fact]
    public async Task Keyboard_charging_flag()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.On(RogProtocol.KeyboardPowerInfo, KeyboardPower(0x02, 45, charging: 1));

        var reading = await PollSingle();

        Assert.Equal([BatteryComponent.Single(45, ChargeState.Charging)], reading.Components);
    }

    [Fact]
    public async Task Keyboard_unknown_charging_value_is_unknown()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.On(RogProtocol.KeyboardPowerInfo, KeyboardPower(0x02, 45, charging: 7));

        var reading = await PollSingle();

        Assert.Equal([BatteryComponent.Single(45, ChargeState.Unknown)], reading.Components);
    }

    [Fact]
    public async Task Keyboard_percent_above_100_is_online_without_a_battery_value()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.On(RogProtocol.KeyboardPowerInfo, KeyboardPower(0x02, 0xC8, charging: 0));

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Empty(reading.Components);
        Assert.NotNull(reading.Detail);
    }

    [Fact]
    public async Task Keyboard_wired_through_the_receiver_and_not_charging_is_on_power()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.On(RogProtocol.KeyboardConnectionType, R(0x02, 0x12, 0x03, 0x00, 0x00, 0x00));

        var reading = await PollSingle();

        Assert.Equal([BatteryComponent.Single(45, ChargeState.NotCharging)], reading.Components);
    }

    [Fact]
    public async Task Power_reject_means_asleep()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.On(RogProtocol.KeyboardPowerInfo, R(0x02, 0xFF, 0xAA));

        var reading = await PollSingle();

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.NotNull(reading.Detail);
    }

    [Fact]
    public async Task Connection_type_FF_means_asleep_and_power_is_not_asked()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.On(RogProtocol.KeyboardConnectionType, R(0x02, 0x12, 0x03, 0x00, 0x00, 0xFF));

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Equal([RogProtocol.KeyboardConnectionType], omni.KeyboardSlot.Commands);
    }

    [Fact]
    public async Task Connection_type_reject_means_asleep()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.On(RogProtocol.KeyboardConnectionType, R(0x02, 0xFF, 0xAA));

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Equal([RogProtocol.KeyboardConnectionType], omni.KeyboardSlot.Commands);
    }

    [Fact]
    public async Task Silent_keyboard_is_retried_once_then_asleep()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.Silent(RogProtocol.KeyboardConnectionType);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Equal([RogProtocol.KeyboardConnectionType, RogProtocol.KeyboardConnectionType], omni.KeyboardSlot.Commands);
    }

    [Fact]
    public async Task Late_reply_on_the_retry_is_used()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.OnSequence(RogProtocol.KeyboardPowerInfo, [], [KeyboardPower45]);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal(45, reading.LowestPercent);
    }

    [Fact]
    public async Task Foreign_frames_on_the_same_collection_are_skipped()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.On(RogProtocol.KeyboardPowerInfo,
            R(0x02, 0x12, 0x07, 0x00, 0x00, 0x01),                         // vendor software's 12 07 reply
            R(0x02, 0x12, 0x03, 0x00, 0x00, 0x01),                         // a late copy of our previous reply
            R(0x02, 0x12, 0x01, 0x01, 0x00, 0x00, 0x63),                   // same command, other index
            R(0x04, 0x12, 0x01, 0x00, 0x00, 0x00, 0x63),                   // another report id
            R(0x02, 0x50, 0x01, 0x00, 0x00, 0x00, 0x63),                   // a SET echo from vendor software
            KeyboardPower45);

        var reading = await PollSingle();

        Assert.Equal(45, reading.LowestPercent);
    }

    [Fact]
    public async Task Foreign_reject_before_our_reply_is_skipped()
    {
        var omni = new FakeOmni(_backend);
        // Gear Link's 12 00 0E is rejected on the same handle (rog.md 5e, 9) just before our 12 01 reply arrives.
        omni.KeyboardSlot.On(RogProtocol.KeyboardPowerInfo, R(0x02, 0xFF, 0xAA), KeyboardPower45);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(45, ChargeState.Discharging)], reading.Components);
        Assert.Equal([RogProtocol.KeyboardConnectionType, RogProtocol.KeyboardPowerInfo], omni.KeyboardSlot.Commands);
    }

    [Fact]
    public async Task Stale_reports_queued_before_the_request_are_flushed()
    {
        var omni = new FakeOmni(_backend);
        // An old 12 01 reply still sits in the queue after the 12 03 exchange; it must not answer our 12 01.
        omni.KeyboardSlot.On(RogProtocol.KeyboardConnectionType, ConnectionRf, KeyboardPower(0x02, 99, charging: 0));

        var reading = await PollSingle();

        Assert.Equal(45, reading.LowestPercent);
    }

    [Fact]
    public async Task Unexpected_short_power_reply_is_an_error()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.On(RogProtocol.KeyboardPowerInfo, [0x02, 0x12, 0x01, 0x00, 0x00, 0x00, 0x2D]);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.NotNull(reading.Detail);
    }

    // --- Device problems end the poll quietly ---

    [Theory]
    [InlineData(1167)]
    [InlineData(6)]
    [InlineData(995)]
    [InlineData(31)]
    public async Task Keyboard_gone_mid_read_is_asleep(int win32Error)
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.Device.ReadError = new HidIoException("ReadFile failed", win32Error);

        var reading = await PollSingle();

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.NotNull(reading.Detail);
    }

    [Fact]
    public async Task Keyboard_gone_mid_write_is_asleep()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.Device.WriteError = new HidIoException("WriteFile failed", 1167);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
    }

    [Fact]
    public async Task Other_io_failure_is_an_error()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.Device.ReadError = new HidIoException("ReadFile failed", 87);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.NotNull(reading.Detail);
    }

    [Fact]
    public async Task Receiver_unplugged_mid_scan_gives_no_readings()
    {
        var omni = new FakeOmni(_backend);
        omni.Receiver.Device.ReadError = new HidIoException("ReadFile failed", 1167);

        Assert.Empty(await Poll());
        Assert.Empty(omni.KeyboardSlot.Device.Opens);
    }

    [Fact]
    public async Task Keyboard_slot_access_denied_needs_access()
    {
        var omni = new FakeOmni(_backend);
        omni.KeyboardSlot.Device.OpenError = new HidIoException("Cannot open", HidIoException.AccessDenied);

        var reading = await PollSingle();

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal(DeviceStatus.NeedsAccess, reading.Status);
        Assert.NotNull(reading.Detail);
    }

    [Fact]
    public async Task Receiver_access_denied_is_reported_on_the_receiver()
    {
        var omni = new FakeOmni(_backend);
        omni.Receiver.Device.OpenError = new HidIoException("Cannot open", HidIoException.AccessDenied);

        var reading = await PollSingle();

        Assert.Equal("rog:omni-receiver:T4MPKRD01THC", reading.DeviceId);
        Assert.Equal("ROG OMNI Receiver", reading.Name);
        Assert.Equal(DeviceKind.Other, reading.Kind);
        Assert.Equal(DeviceStatus.NeedsAccess, reading.Status);
    }

    [Fact]
    public async Task Receiver_that_does_not_answer_the_scan_is_an_error()
    {
        var omni = new FakeOmni(_backend);
        omni.Receiver.Silent(RogProtocol.ScanForPairedDevices);

        var reading = await PollSingle();

        Assert.Equal("rog:omni-receiver:T4MPKRD01THC", reading.DeviceId);
        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal([RogProtocol.ScanForPairedDevices, RogProtocol.ScanForPairedDevices], omni.Receiver.Commands);
    }

    [Fact]
    public async Task Unsafe_command_is_an_internal_error_and_nothing_is_sent()
    {
        var omni = new FakeOmni(_backend);
        var provider = new RogBatteryProvider(_backend, _time, new RefuseKeyboardPower());

        var reading = Assert.Single(await provider.PollAsync(CancellationToken.None));

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Internal error: unsafe command refused", reading.Detail);
        Assert.Equal([RogProtocol.KeyboardConnectionType], omni.KeyboardSlot.Commands);
    }

    [Fact]
    public async Task Unsafe_scan_is_an_internal_error_on_the_receiver()
    {
        var omni = new FakeOmni(_backend);
        var provider = new RogBatteryProvider(_backend, _time, new RefuseAll());

        var reading = Assert.Single(await provider.PollAsync(CancellationToken.None));

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Internal error: unsafe command refused", reading.Detail);
        Assert.Empty(omni.Receiver.Device.Written);
    }

    // --- A receiver that fails after a good scan reports the devices it listed, not itself ---

    [Fact]
    public async Task Scan_without_an_answer_after_a_good_poll_is_reported_on_the_listed_devices()
    {
        var omni = WithKeyboardAndMouse();
        var provider = Provider();
        await provider.PollAsync(CancellationToken.None);
        omni.Receiver.Silent(RogProtocol.ScanForPairedDevices);

        var readings = await provider.PollAsync(CancellationToken.None);

        Assert.Equal([AzothXId, MouseId], readings.Select(r => r.DeviceId));
        Assert.Equal(["ROG Azoth X", "ROG mouse"], readings.Select(r => r.Name));
        Assert.Equal([DeviceKind.Keyboard, DeviceKind.Mouse], readings.Select(r => r.Kind));
        Assert.All(readings, r =>
        {
            Assert.Equal(DeviceStatus.Error, r.Status);
            Assert.Equal("Receiver did not answer", r.Detail);
            Assert.Empty(r.Components);
        });
        Assert.Single(omni.KeyboardSlot.Device.Opens);
        Assert.Single(omni.MouseSlot.Device.Opens);
    }

    [Fact]
    public async Task Rejected_scan_after_a_good_poll_is_reported_on_the_listed_devices()
    {
        var omni = new FakeOmni(_backend);
        var provider = Provider();
        await provider.PollAsync(CancellationToken.None);
        omni.Receiver.On(RogProtocol.ScanForPairedDevices, R(0x01, 0xFF, 0xAA));

        var reading = Assert.Single(await provider.PollAsync(CancellationToken.None));

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Receiver did not answer", reading.Detail);
    }

    [Fact]
    public async Task Receiver_io_error_after_a_good_poll_is_reported_on_the_listed_devices()
    {
        var omni = new FakeOmni(_backend);
        var provider = Provider();
        await provider.PollAsync(CancellationToken.None);
        omni.Receiver.Device.ReadError = new HidIoException("ReadFile failed", 87);

        var reading = Assert.Single(await provider.PollAsync(CancellationToken.None));

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Read failed (error 87)", reading.Detail);
    }

    [Fact]
    public async Task Receiver_access_denied_after_a_good_poll_is_reported_on_the_listed_devices()
    {
        var omni = new FakeOmni(_backend);
        var provider = Provider();
        await provider.PollAsync(CancellationToken.None);
        omni.Receiver.Device.OpenError = new HidIoException("Cannot open", HidIoException.AccessDenied);

        var reading = Assert.Single(await provider.PollAsync(CancellationToken.None));

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal(DeviceStatus.NeedsAccess, reading.Status);
        Assert.Equal("Windows denied access to the receiver", reading.Detail);
    }

    [Fact]
    public async Task Refused_scan_after_a_good_poll_is_reported_on_the_listed_devices()
    {
        _ = new FakeOmni(_backend);
        var policy = new SwitchablePolicy();
        var provider = new RogBatteryProvider(_backend, _time, policy);
        await provider.PollAsync(CancellationToken.None);
        policy.Refuse = true;

        var reading = Assert.Single(await provider.PollAsync(CancellationToken.None));

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Internal error: unsafe command refused", reading.Detail);
    }

    [Fact]
    public async Task Scan_failure_reports_the_devices_of_the_latest_good_scan()
    {
        var omni = WithKeyboardAndMouse();
        var provider = Provider();
        await provider.PollAsync(CancellationToken.None);
        omni.Receiver.On(RogProtocol.ScanForPairedDevices, ScanAzothX);
        await provider.PollAsync(CancellationToken.None);
        omni.Receiver.Silent(RogProtocol.ScanForPairedDevices);

        var reading = Assert.Single(await provider.PollAsync(CancellationToken.None));

        Assert.Equal(AzothXId, reading.DeviceId);
    }

    [Fact]
    public async Task Scan_failure_after_a_scan_that_listed_nothing_is_reported_on_the_receiver()
    {
        var omni = new FakeOmni(_backend);
        omni.Receiver.On(RogProtocol.ScanForPairedDevices, R(0x01, 0xA0, 0x00, 0x00, 0x00));
        var provider = Provider();
        await provider.PollAsync(CancellationToken.None);
        omni.Receiver.Silent(RogProtocol.ScanForPairedDevices);

        var reading = Assert.Single(await provider.PollAsync(CancellationToken.None));

        Assert.Equal("rog:omni-receiver:T4MPKRD01THC", reading.DeviceId);
        Assert.Equal(DeviceStatus.Error, reading.Status);
    }

    [Fact]
    public async Task Scan_failure_reports_only_the_devices_of_the_receiver_that_failed()
    {
        _ = new FakeOmni(_backend);
        var other = new FakeOmni(_backend, serial: "R2", pid: 0x1ACF);
        var provider = Provider();
        await provider.PollAsync(CancellationToken.None);
        other.Receiver.Silent(RogProtocol.ScanForPairedDevices);

        var readings = await provider.PollAsync(CancellationToken.None);

        Assert.Equal([AzothXId, "rog:azoth-x:R2"], readings.Select(r => r.DeviceId));
        Assert.Equal([DeviceStatus.Online, DeviceStatus.Error], readings.Select(r => r.Status));
    }

    [Fact]
    public async Task Receiver_unplugged_after_a_good_poll_gives_no_readings()
    {
        var omni = new FakeOmni(_backend);
        var provider = Provider();
        await provider.PollAsync(CancellationToken.None);
        omni.Receiver.Device.ReadError = new HidIoException("ReadFile failed", 1167);

        Assert.Empty(await provider.PollAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_poll_throws_and_sends_nothing()
    {
        var omni = new FakeOmni(_backend);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().PollAsync(cts.Token));
        Assert.Empty(omni.Receiver.Device.Written);
    }

    [Fact]
    public async Task Each_poll_opens_and_closes_its_connections()
    {
        var omni = new FakeOmni(_backend);
        var provider = Provider();

        await provider.PollAsync(CancellationToken.None);
        var second = Assert.Single(await provider.PollAsync(CancellationToken.None));

        Assert.Equal(DeviceStatus.Online, second.Status);
        Assert.Equal(2, omni.Receiver.Device.Opens.Count);
        Assert.Equal(2, omni.KeyboardSlot.Device.Opens.Count);
    }

    // --- Serial / device id ---

    [Fact]
    public async Task Receiver_without_a_usb_serial_asks_A1_00()
    {
        var omni = new FakeOmni(_backend, serial: null);

        var reading = await PollSingle();

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal([RogProtocol.ReceiverSerialNumber, RogProtocol.ScanForPairedDevices], omni.Receiver.Commands);
    }

    [Fact]
    public async Task Receiver_without_any_serial_falls_back_to_its_product_id()
    {
        var omni = new FakeOmni(_backend, serial: null);
        omni.Receiver.On(RogProtocol.ReceiverSerialNumber, R(0x01, 0xFF, 0xAA));

        var reading = await PollSingle();

        Assert.Equal("rog:azoth-x:1ACE", reading.DeviceId);
    }

    // --- Mouse slot ---

    [Fact]
    public async Task Mouse_slot_reads_exist_then_power()
    {
        var omni = new FakeOmni(_backend);
        omni.Receiver.On(RogProtocol.ScanForPairedDevices,
            R(0x01, 0xA0, 0x00, 0x02, 0x00, 0x25, 0x1C, 0x02, 0x04, 0xB0, 0x1A, 0x03, 0x05));
        omni.MouseSlot.On(RogProtocol.MouseExistInfo, R(0x03, 0x12, 0x00, 0x02, 0x00, 0x01))
            .On(RogProtocol.MousePowerInfo, MousePower(0x03, 80, charging: 1));

        var readings = await Poll();

        Assert.Equal(2, readings.Count);
        var mouse = Assert.Single(readings, r => r.Kind == DeviceKind.Mouse);
        Assert.Equal("rog:1ab0:T4MPKRD01THC", mouse.DeviceId);
        Assert.Equal("ROG mouse", mouse.Name);
        Assert.Equal(DeviceStatus.Online, mouse.Status);
        Assert.Equal([BatteryComponent.Single(80, ChargeState.Charging)], mouse.Components);
        Assert.Equal("ROG 12 07", mouse.Source);
        Assert.Equal([RogProtocol.MouseExistInfo, RogProtocol.MousePowerInfo], omni.MouseSlot.Commands);
        Assert.All(omni.MouseSlot.Device.Written, frame => Assert.Equal(0x03, frame[0]));
    }

    [Fact]
    public async Task Mouse_zero_percent_and_not_charging_is_asleep_not_empty()
    {
        var omni = WithMouseSlot();
        omni.MouseSlot.On(RogProtocol.MouseExistInfo, R(0x03, 0x12, 0x00, 0x02, 0x00, 0x01))
            .On(RogProtocol.MousePowerInfo, MousePower(0x03, 0, charging: 0));

        var mouse = Assert.Single(await Poll());

        Assert.Equal(DeviceStatus.Asleep, mouse.Status);
        Assert.Empty(mouse.Components);
    }

    [Fact]
    public async Task Mouse_not_existing_is_asleep_and_power_is_not_asked()
    {
        var omni = WithMouseSlot();

        var mouse = Assert.Single(await Poll());

        Assert.Equal(DeviceStatus.Asleep, mouse.Status);
        Assert.Equal([RogProtocol.MouseExistInfo], omni.MouseSlot.Commands);
    }

    [Fact]
    public async Task Mouse_power_reject_is_asleep()
    {
        var omni = WithMouseSlot();
        omni.MouseSlot.On(RogProtocol.MouseExistInfo, R(0x03, 0x12, 0x00, 0x02, 0x00, 0x01));

        var mouse = Assert.Single(await Poll());

        Assert.Equal(DeviceStatus.Asleep, mouse.Status);
    }

    [Fact]
    public async Task Slot_without_its_control_collection_is_an_error()
    {
        var omni = new FakeOmni(_backend);
        omni.Receiver.On(RogProtocol.ScanForPairedDevices, R(0x01, 0xA0, 0x00, 0x01, 0x00, 0x25, 0x1C, 0x06, 0x07));

        var reading = await PollSingle();

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Empty(omni.KeyboardSlot.Device.Opens);
    }

    [Fact]
    public async Task Receiver_with_no_paired_devices_gives_no_readings()
    {
        var omni = new FakeOmni(_backend);
        omni.Receiver.On(RogProtocol.ScanForPairedDevices, R(0x01, 0xA0, 0x00, 0x00, 0x00));

        Assert.Empty(await Poll());
        Assert.Empty(omni.KeyboardSlot.Device.Opens);
    }

    // --- Directly attached devices (unverified on hardware) ---

    [Fact]
    public async Task Wired_azoth_x_uses_report_id_0_skips_the_alive_check_and_is_on_power()
    {
        var wired = WiredAzothX();
        wired.On(RogProtocol.KeyboardPowerInfo, R65(0x00, 0x12, 0x01, 0x00, 0x00, 0x00, 60, 0x03, 0x00, 0x00));

        var reading = await PollSingle();

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(60, ChargeState.NotCharging)], reading.Components);
        var frame = Assert.Single(wired.Device.Written);
        Assert.Equal(65, frame.Length);
        Assert.Equal(new byte[] { 0x00, 0x12, 0x01, 0x00, 0x00 }, frame[..5]);
    }

    [Fact]
    public async Task Wired_azoth_x_full()
    {
        var wired = WiredAzothX();
        wired.On(RogProtocol.KeyboardPowerInfo, R65(0x00, 0x12, 0x01, 0x00, 0x00, 0x00, 100, 0x03, 0x00, 0x00));

        var reading = await PollSingle();

        Assert.Equal([BatteryComponent.Single(100, ChargeState.Full)], reading.Components);
    }

    [Fact]
    public async Task Wired_and_omni_paths_of_one_keyboard_are_one_device_preferring_the_wired_reading()
    {
        _ = new FakeOmni(_backend);
        var wired = WiredAzothX();
        wired.On(RogProtocol.KeyboardPowerInfo, R65(0x00, 0x12, 0x01, 0x00, 0x00, 0x00, 46, 0x03, 0x00, 0x01));

        var reading = await PollSingle();

        Assert.Equal(AzothXId, reading.DeviceId);
        Assert.Equal([BatteryComponent.Single(46, ChargeState.Charging)], reading.Components);
    }

    [Fact]
    public async Task Online_omni_reading_wins_over_a_failed_wired_one()
    {
        _ = new FakeOmni(_backend);
        var wired = WiredAzothX();
        wired.Device.ReadError = new HidIoException("ReadFile failed", 1167);

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal(45, reading.LowestPercent);
    }

    [Fact]
    public async Task Bluetooth_azoth_x_is_not_polled()
    {
        var bt = _backend.Add(0x0B05, 0x1C27, 0xFF00, 1, 64, 64, outputReportId: 2, serial: FakeOmni.Serial);

        Assert.Empty(await Poll());
        Assert.Empty(bt.Opens);
    }

    [Fact]
    public async Task Own_dongle_mouse_uses_report_id_0()
    {
        var mouse = new RogResponder(_backend.Add(0x0B05, 0x1960, 0xFF01, 1, 65, 65, mi: 1, serial: "K1", outputReportId: 0));
        mouse.On(RogProtocol.MouseExistInfo, R65(0x00, 0x12, 0x00, 0x02, 0x00, 0x01))
            .On(RogProtocol.MousePowerInfo, R65(0x00, 0x12, 0x07, 0x00, 0x00, 70, 0x02, 0x0A, 0xD0, 0x0E, 0x00, 0x00));

        var reading = await PollSingle();

        Assert.Equal("rog:keris-wireless:K1", reading.DeviceId);
        Assert.Equal("ROG Keris Wireless", reading.Name);
        Assert.Equal([BatteryComponent.Single(70, ChargeState.Discharging)], reading.Components);
        Assert.Equal([RogProtocol.MouseExistInfo, RogProtocol.MousePowerInfo], mouse.Commands);
        Assert.All(mouse.Device.Written, frame => Assert.Equal(0x00, frame[0]));
    }

    [Fact]
    public async Task Cetra_reads_left_right_and_case()
    {
        var cetra = Cetra();
        cetra.On(RogProtocol.CetraBatteryLevel, R(0xCC, 0x12, 0x12, 0x00, 0x00, 0x4B, 0x4C, 0x64))
            .On(RogProtocol.CetraChargingStatus, R(0xCC, 0x12, 0x11, 0x00, 0x00, 0x03, 0x00));

        var reading = await PollSingle();

        Assert.Equal("rog:cetra-open-wireless:C1", reading.DeviceId);
        Assert.Equal("ROG Cetra Open Wireless", reading.Name);
        Assert.Equal(DeviceKind.Earbuds, reading.Kind);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal("ROG CC 12 12", reading.Source);
        Assert.Equal(
            [
                new BatteryComponent("Left", 75, ChargeState.Charging),
                new BatteryComponent("Right", 76, ChargeState.Charging),
                new BatteryComponent("Case", 100, ChargeState.Discharging),
            ],
            reading.Components);
        Assert.Equal([RogProtocol.CetraBatteryLevel, RogProtocol.CetraChargingStatus], cetra.Commands);
        Assert.All(cetra.Device.Written, frame => Assert.Equal(0xCC, frame[0]));
    }

    [Fact]
    public async Task Cetra_without_charging_status_has_unknown_charge_and_unknown_values_stay_null()
    {
        var cetra = Cetra();
        cetra.On(RogProtocol.CetraBatteryLevel, R(0xCC, 0x12, 0x12, 0x00, 0x00, 0x4B, 0xFF, 0x64));

        var reading = await PollSingle();

        Assert.Equal(
            [
                new BatteryComponent("Left", 75, ChargeState.Unknown),
                new BatteryComponent("Right", null, ChargeState.Unknown),
                new BatteryComponent("Case", 100, ChargeState.Unknown),
            ],
            reading.Components);
    }

    [Fact]
    public async Task Cetra_with_no_known_values_is_asleep()
    {
        var cetra = Cetra();
        cetra.On(RogProtocol.CetraBatteryLevel, R(0xCC, 0x12, 0x12, 0x00, 0x00, 0xFF, 0xFF, 0xFF));

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
        Assert.Empty(reading.Components);
    }

    [Fact]
    public async Task Cetra_reject_is_asleep()
    {
        var cetra = Cetra();
        cetra.On(RogProtocol.CetraBatteryLevel, R(0xCC, 0xFF, 0xAA));

        var reading = await PollSingle();

        Assert.Equal(DeviceStatus.Asleep, reading.Status);
    }

    [Fact]
    public async Task Cetra_collection_without_report_CC_is_skipped()
    {
        var other = _backend.Add(0x0B05, 0x1CAC, 0xFF00, 1, 64, 64, serial: "C1", outputReportId: 0x02);

        Assert.Empty(await Poll());
        Assert.Empty(other.Opens);
    }

    // --- Helpers ---

    private FakeOmni WithKeyboardAndMouse()
    {
        var omni = new FakeOmni(_backend);
        omni.Receiver.On(RogProtocol.ScanForPairedDevices,
            R(0x01, 0xA0, 0x00, 0x02, 0x00, 0x25, 0x1C, 0x02, 0x04, 0xB0, 0x1A, 0x03, 0x05));
        omni.MouseSlot.On(RogProtocol.MouseExistInfo, R(0x03, 0x12, 0x00, 0x02, 0x00, 0x01))
            .On(RogProtocol.MousePowerInfo, MousePower(0x03, 80, charging: 1));
        return omni;
    }

    private FakeOmni WithMouseSlot()
    {
        var omni = new FakeOmni(_backend);
        omni.Receiver.On(RogProtocol.ScanForPairedDevices, R(0x01, 0xA0, 0x00, 0x01, 0x00, 0xB0, 0x1A, 0x03, 0x05));
        return omni;
    }

    // The wired Azoth X 0B05:1C24: MI_01 vendor collection, report id 0, 65-byte reports (G-Helper; unverified here).
    private RogResponder WiredAzothX() =>
        new(_backend.Add(0x0B05, 0x1C24, 0xFF00, 1, 65, 65, mi: 1, product: "ROG AZOTH X", serial: FakeOmni.Serial, outputReportId: 0));

    private RogResponder Cetra() =>
        new(_backend.Add(0x0B05, 0x1CAC, 0xFF00, 1, 64, 64, serial: "C1", outputReportId: RogProtocol.CetraReportId));

    private sealed class RefuseAll : ICommandPolicy
    {
        public bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing) => false;
    }

    private sealed class RefuseKeyboardPower : ICommandPolicy
    {
        private readonly RogCommandPolicy _inner = new();

        public bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing) =>
            !(outgoing[1] == 0x12 && outgoing[2] == 0x01) && _inner.IsAllowed(device, outgoing);
    }

    private sealed class SwitchablePolicy : ICommandPolicy
    {
        private readonly RogCommandPolicy _inner = new();

        public bool Refuse { get; set; }

        public bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing) => !Refuse && _inner.IsAllowed(device, outgoing);
    }
}
