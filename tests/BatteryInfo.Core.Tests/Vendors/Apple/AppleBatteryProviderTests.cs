using BatteryInfo.Core.Model;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Apple;

namespace BatteryInfo.Core.Tests.Vendors.Apple;

public class AppleBatteryProviderTests
{
    private const ulong Address = 0x282D7FB83C8B;
    private const ulong OtherAddress = 0x112233445566;
    private const ulong Pods = 0x4A1111111111;
    private const ulong Stranger = 0x7B3333333333;

    // AirPods Pro 2 (14 20), sent by the right pod: left 90, right 80, case not reported. The tail is zeroed.
    private const string ProTwo = "07 19 01 14 20 0B 98 8F 01 00 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";
    private const string ProTwoLower = "07 19 01 14 20 0B 87 8F 01 00 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";
    private const string StrangerFrame = "07 19 01 14 20 2B 33 8F 01 00 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";

    private static readonly IReadOnlyList<BatteryComponent> Ninety80 =
    [
        new("Left", 90, ChargeState.Unknown),
        new("Right", 80, ChargeState.Unknown),
        new("Case", null, ChargeState.Unknown),
    ];

    private readonly ManualTimeProvider _time = new();
    private readonly FakeAppleBluetooth _bluetooth = new();

    public AppleBatteryProviderTests()
    {
        _bluetooth.Paired.Add(new AppleDevice(Address, "David's AirPods Pro", 0x2014));
    }

    private AppleBatteryProvider CreateProvider(TimeSpan? firstFrameWait = null) =>
        new(_bluetooth, _time, firstFrameWait ?? TimeSpan.Zero);

    private static async Task<DeviceReading> PollOne(AppleBatteryProvider provider) =>
        Assert.Single(await provider.PollAsync(CancellationToken.None));

    private void Wait(double seconds) => _time.Advance(TimeSpan.FromSeconds(seconds));

    /// <summary>The pods are already connected when the provider first looks: their address is chosen by signal
    /// strength once every address in range had a few seconds to be heard.</summary>
    private async Task<AppleBatteryProvider> ConnectedProvider(string frame = ProTwo)
    {
        _bluetooth.Connected.Add(Address);
        var provider = CreateProvider();
        await provider.PollAsync(CancellationToken.None);
        _bluetooth.Advertise(Pods, -60, frame);
        Wait(6);
        _bluetooth.Advertise(Pods, -60, frame);
        return provider;
    }

    [Fact]
    public async Task Without_paired_apple_devices_there_is_nothing_and_no_scan()
    {
        _bluetooth.Paired.Clear();
        using var provider = CreateProvider();

        Assert.Empty(await provider.PollAsync(CancellationToken.None));
        Assert.False(_bluetooth.IsListening);
    }

    [Fact]
    public async Task Paired_but_not_connected_gives_no_reading_but_listens()
    {
        using var provider = CreateProvider();

        Assert.Empty(await provider.PollAsync(CancellationToken.None));
        Assert.True(_bluetooth.IsListening);
    }

    [Fact]
    public async Task Connected_pods_report_left_right_and_case_from_their_broadcast()
    {
        using var provider = await ConnectedProvider();

        var reading = await PollOne(provider);

        Assert.Equal("apple:282D7FB83C8B", reading.DeviceId);
        Assert.Equal("Apple", reading.Vendor);
        Assert.Equal("David's AirPods Pro", reading.Name);
        Assert.Equal(DeviceKind.Earbuds, reading.Kind);
        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal(Ninety80, reading.Components);
        Assert.Equal(DeviceReading.AppleBroadcastSource, reading.Source);
        Assert.True(reading.Approximate);
        Assert.Null(reading.Detail);
        Assert.Equal(_time.GetUtcNow(), reading.Timestamp);
    }

    [Fact]
    public async Task Pods_that_connect_while_it_runs_are_found_by_their_new_address()
    {
        using var provider = CreateProvider();
        await provider.PollAsync(CancellationToken.None);
        _bluetooth.Advertise(Stranger, -40, StrangerFrame);
        Wait(120);

        _bluetooth.Connected.Add(Address);
        await provider.PollAsync(CancellationToken.None);
        _bluetooth.Advertise(Stranger, -40, StrangerFrame);
        _bluetooth.Advertise(Stranger, -40, StrangerFrame);
        _bluetooth.Advertise(Pods, -88, ProTwo);
        _bluetooth.Advertise(Pods, -88, ProTwo);

        Assert.Equal(Ninety80, (await PollOne(provider)).Components);
    }

    [Fact]
    public async Task Addresses_in_range_when_the_scan_restarts_are_not_taken_as_new_at_the_connect()
    {
        using var provider = CreateProvider();
        await provider.PollAsync(CancellationToken.None);
        Wait(10 * 60);
        _bluetooth.FailListening("RadioNotAvailable");
        Wait(61);
        await provider.PollAsync(CancellationToken.None);
        Assert.True(_bluetooth.IsListening);
        _bluetooth.Advertise(Stranger, -40, StrangerFrame);
        Wait(5);

        _bluetooth.Connected.Add(Address);
        await provider.PollAsync(CancellationToken.None);
        _bluetooth.Advertise(Stranger, -40, StrangerFrame);
        _bluetooth.Advertise(Stranger, -40, StrangerFrame);

        Assert.Empty((await PollOne(provider)).Components);
    }

    [Fact]
    public async Task Connected_without_a_broadcast_yet_shows_no_level_and_says_it_waits()
    {
        _bluetooth.Connected.Add(Address);
        using var provider = CreateProvider();

        var reading = await PollOne(provider);

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Empty(reading.Components);
        Assert.False(reading.Approximate);
        Assert.Equal("Waiting for its Bluetooth battery broadcast", reading.Detail);
    }

    [Fact]
    public async Task Windows_own_value_is_shown_while_no_broadcast_is_fresh()
    {
        _bluetooth.Connected.Add(Address);
        _bluetooth.WindowsBattery[Address] = 70;
        using var provider = CreateProvider();

        var reading = await PollOne(provider);

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Equal([BatteryComponent.Single(70, ChargeState.Unknown)], reading.Components);
        Assert.Equal(DeviceReading.WindowsHfpSource, reading.Source);
        Assert.True(reading.Approximate);
    }

    [Fact]
    public async Task A_fresh_broadcast_wins_over_windows_value()
    {
        _bluetooth.WindowsBattery[Address] = 70;
        using var provider = await ConnectedProvider();

        Assert.Equal(DeviceReading.AppleBroadcastSource, (await PollOne(provider)).Source);
    }

    [Fact]
    public async Task A_broadcast_silent_for_minutes_leaves_no_level_and_says_since_when()
    {
        using var provider = await ConnectedProvider();
        Wait(4 * 60);

        var reading = await PollOne(provider);

        Assert.Equal(DeviceStatus.Online, reading.Status);
        Assert.Empty(reading.Components);
        Assert.Equal("No battery broadcast for 4 min", reading.Detail);
    }

    [Fact]
    public async Task A_held_case_level_does_not_keep_silent_pods_shown_as_live()
    {
        const string inTheCase = "07 19 01 14 20 55 99 F6 51 00 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"; // case 60
        using var provider = await ConnectedProvider(inTheCase);
        Wait(4 * 60);

        var silent = await PollOne(provider);
        Assert.Empty(silent.Components);
        Assert.Equal("No battery broadcast for 4 min", silent.Detail);

        _bluetooth.WindowsBattery[Address] = 70;
        Assert.Equal(DeviceReading.WindowsHfpSource, (await PollOne(provider)).Source);
    }

    [Fact]
    public async Task A_remembered_case_level_says_when_it_was_read()
    {
        const string inTheCase = "07 19 01 14 20 55 99 F6 51 00 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"; // case 60
        using var provider = CreateProvider();
        await provider.PollAsync(CancellationToken.None);
        Wait(60);
        _bluetooth.Connected.Add(Address); // the lid opens: the pods connect
        await provider.PollAsync(CancellationToken.None);
        _bluetooth.Advertise(Pods, -50, inTheCase);
        _bluetooth.Advertise(Pods, -50, inTheCase);
        var readAt = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone);
        _bluetooth.Connected.Remove(Address);
        await provider.PollAsync(CancellationToken.None);
        Wait(60 * 60);

        _bluetooth.Connected.Add(Address);
        await provider.PollAsync(CancellationToken.None);
        _bluetooth.Advertise(Pods, -60, ProTwo);
        _bluetooth.Advertise(Pods, -60, ProTwo);
        var reading = await PollOne(provider);

        Assert.Equal([90, 80, 60], reading.Components.Select(c => c.Percent));
        Assert.Equal($"Case read at {readAt:HH:mm}", reading.Detail);
    }

    [Fact]
    public async Task New_levels_are_taken_in_as_they_are_broadcast()
    {
        using var provider = await ConnectedProvider();
        Wait(30);

        _bluetooth.Advertise(Pods, -60, ProTwoLower);

        Assert.Equal([80, 70, null], (await PollOne(provider)).Components.Select(c => c.Percent));
    }

    [Fact]
    public async Task Frames_of_another_model_do_not_reach_the_device()
    {
        _bluetooth.Connected.Add(Address);
        using var provider = CreateProvider();
        await provider.PollAsync(CancellationToken.None);
        var proOne = ProTwo.Replace("14 20", "0E 20", StringComparison.Ordinal);

        _bluetooth.Advertise(Pods, -60, proOne);
        Wait(6);
        _bluetooth.Advertise(Pods, -60, proOne);

        Assert.Empty((await PollOne(provider)).Components);
    }

    [Fact]
    public async Task A_disconnect_gives_no_reading_and_a_reconnect_starts_over()
    {
        using var provider = await ConnectedProvider();
        _bluetooth.Connected.Remove(Address);
        Assert.Empty(await provider.PollAsync(CancellationToken.None));

        _bluetooth.Connected.Add(Address);
        var reading = await PollOne(provider);

        Assert.Empty(reading.Components);
        Assert.Equal("Waiting for its Bluetooth battery broadcast", reading.Detail);
    }

    [Fact]
    public async Task A_model_without_a_known_layout_is_an_error()
    {
        _bluetooth.Paired[0] = new AppleDevice(Address, "AirPods", 0x2028);
        _bluetooth.Connected.Add(Address);
        using var provider = CreateProvider();

        var reading = await PollOne(provider);

        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Apple model 0x2028 is not supported yet", reading.Detail);
    }

    [Fact]
    public async Task Two_connected_devices_of_one_model_are_errors()
    {
        _bluetooth.Paired.Add(new AppleDevice(OtherAddress, "Work AirPods Pro", 0x2014));
        _bluetooth.Connected.Add(Address);
        _bluetooth.Connected.Add(OtherAddress);
        using var provider = CreateProvider();

        var readings = await provider.PollAsync(CancellationToken.None);

        Assert.Equal(2, readings.Count);
        Assert.All(readings, r =>
        {
            Assert.Equal(DeviceStatus.Error, r.Status);
            Assert.Equal("Two connected devices of this model cannot be told apart", r.Detail);
        });
    }

    [Fact]
    public async Task One_battery_models_report_a_single_level()
    {
        _bluetooth.Paired[0] = new AppleDevice(Address, "AirPods Max", 0x201F);
        using var provider = await ConnectedProvider(AppleVectors.OneBatteryFrames.Single(f => f.Name == "Orbit_AirPodsMaxUsbc_37pct_Windows").Hex);

        var reading = await PollOne(provider);

        Assert.Equal(DeviceKind.Headset, reading.Kind);
        Assert.Equal([BatteryComponent.Single(30, ChargeState.Unknown)], reading.Components);
    }

    [Fact]
    public async Task The_find_my_suffix_is_left_out_of_the_name()
    {
        _bluetooth.Paired[0] = new AppleDevice(Address, "APP2 - Find My", 0x2014);
        _bluetooth.Connected.Add(Address);
        using var provider = CreateProvider();

        Assert.Equal("APP2", (await PollOne(provider)).Name);
    }

    [Fact]
    public async Task An_unnamed_device_is_named_after_its_model()
    {
        _bluetooth.Paired[0] = new AppleDevice(Address, "", 0x2014);
        _bluetooth.Connected.Add(Address);
        using var provider = CreateProvider();

        Assert.Equal("AirPods Pro 2", (await PollOne(provider)).Name);
    }

    [Fact]
    public async Task A_scan_that_cannot_start_is_shown_on_the_connected_device_and_retried()
    {
        _bluetooth.Connected.Add(Address);
        _bluetooth.StartError = new InvalidOperationException("The radio is off");
        using var provider = CreateProvider();

        var reading = await PollOne(provider);
        Assert.Equal(DeviceStatus.Error, reading.Status);
        Assert.Equal("Bluetooth LE scan failed: The radio is off", reading.Detail);

        _bluetooth.StartError = null;
        Wait(61);
        Assert.Equal(DeviceStatus.Error, (await PollOne(provider)).Status); // until a broadcast proves the scan works
        Assert.True(_bluetooth.IsListening);

        _bluetooth.Advertise(Stranger, -40, StrangerFrame);
        Assert.Equal(DeviceStatus.Online, (await PollOne(provider)).Status);
    }

    [Fact]
    public async Task A_scan_that_stops_on_its_own_is_started_again_a_minute_later()
    {
        using var provider = await ConnectedProvider();
        var refreshes = 0;
        provider.RefreshRequested += (_, _) => refreshes++;
        Wait(4 * 60); // the levels are no longer fresh

        _bluetooth.FailListening("RadioNotAvailable");
        Assert.Equal(1, refreshes);
        var reading = await PollOne(provider);
        Assert.False(_bluetooth.IsListening);
        Assert.Equal("Bluetooth LE scan failed: RadioNotAvailable", reading.Detail);

        Wait(61);
        await provider.PollAsync(CancellationToken.None);
        Assert.True(_bluetooth.IsListening);
        Assert.Equal(2, _bluetooth.Starts);
    }

    [Fact]
    public async Task A_scan_that_keeps_stopping_neither_restarts_nor_asks_for_refreshes_in_a_loop()
    {
        using var provider = await ConnectedProvider();
        var refreshes = 0;
        provider.RefreshRequested += (_, _) => refreshes++;

        for (var i = 0; i < 10; i++)
        {
            _bluetooth.FailListening("RadioNotAvailable");
            Wait(2);
            await provider.PollAsync(CancellationToken.None); // what each refresh would do
        }
        Assert.Equal(1, refreshes);
        Assert.Equal(1, _bluetooth.Starts);

        Wait(61);
        await provider.PollAsync(CancellationToken.None);
        _bluetooth.FailListening("RadioNotAvailable");
        Assert.Equal(2, _bluetooth.Starts);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task A_late_failure_of_an_older_scan_is_ignored_while_a_newer_one_runs()
    {
        using var provider = await ConnectedProvider();
        Wait(4 * 60);

        _bluetooth.FailListening("RadioNotAvailable", stopsScan: false);

        Assert.Equal("No battery broadcast for 4 min", (await PollOne(provider)).Detail);
    }

    [Fact]
    public async Task Stops_listening_once_no_apple_device_is_paired()
    {
        using var provider = CreateProvider();
        await provider.PollAsync(CancellationToken.None);

        _bluetooth.Paired.Clear();
        await provider.PollAsync(CancellationToken.None);

        Assert.False(_bluetooth.IsListening);
    }

    [Fact]
    public async Task Asks_for_a_refresh_on_connection_changes_and_new_levels()
    {
        _bluetooth.Connected.Add(Address);
        using var provider = CreateProvider();
        await provider.PollAsync(CancellationToken.None);
        var refreshes = 0;
        provider.RefreshRequested += (_, _) => refreshes++;

        _bluetooth.RaiseConnectionChanged();
        Assert.Equal(1, refreshes);

        _bluetooth.Advertise(Pods, -60, ProTwo);
        Wait(6);
        _bluetooth.Advertise(Pods, -60, ProTwo);
        Assert.Equal(2, refreshes);

        _bluetooth.Advertise(Pods, -60, ProTwo);
        Assert.Equal(2, refreshes);
    }

    [Fact]
    public async Task The_first_poll_waits_for_a_just_connected_device_s_first_broadcast()
    {
        _bluetooth.Connected.Add(Address);
        using var provider = CreateProvider(firstFrameWait: TimeSpan.FromSeconds(10));
        var poll = provider.PollAsync(CancellationToken.None);
        Assert.False(poll.IsCompleted);

        _bluetooth.Advertise(Pods, -60, ProTwo);
        Wait(6);
        _bluetooth.Advertise(Pods, -60, ProTwo);

        Assert.Equal(Ninety80, Assert.Single(await poll).Components);
    }

    [Fact]
    public async Task The_first_poll_gives_up_waiting_after_its_limit()
    {
        _bluetooth.Connected.Add(Address);
        using var provider = CreateProvider(firstFrameWait: TimeSpan.FromSeconds(10));
        var poll = provider.PollAsync(CancellationToken.None);

        Wait(11);

        Assert.Empty(Assert.Single(await poll).Components);
    }

    [Fact]
    public void By_default_the_first_poll_does_not_wait()
    {
        _bluetooth.Connected.Add(Address);
        using var provider = new AppleBatteryProvider(_bluetooth, _time);

        Assert.True(provider.PollAsync(CancellationToken.None).IsCompleted);
    }

    [Fact]
    public void The_first_poll_does_not_wait_for_a_scan_that_could_not_start()
    {
        _bluetooth.Connected.Add(Address);
        _bluetooth.StartError = new InvalidOperationException("The radio is off");
        using var provider = CreateProvider(firstFrameWait: TimeSpan.FromSeconds(10));

        Assert.True(provider.PollAsync(CancellationToken.None).IsCompleted);
    }

    [Fact]
    public async Task Dispose_stops_the_scan_and_releases_the_bluetooth_side()
    {
        var provider = CreateProvider();
        await provider.PollAsync(CancellationToken.None);

        provider.Dispose();

        Assert.False(_bluetooth.IsListening);
        Assert.True(_bluetooth.Disposed);
    }
}
