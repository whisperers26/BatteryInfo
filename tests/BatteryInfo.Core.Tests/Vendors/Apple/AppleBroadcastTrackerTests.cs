using BatteryInfo.Core.Model;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Apple;

namespace BatteryInfo.Core.Tests.Vendors.Apple;

public class AppleBroadcastTrackerTests
{
    private const ulong Ours = 0x4A1111111111;
    private const ulong OurOtherPod = 0x4A2222222222;
    private const ulong Stranger = 0x7B3333333333;
    private const ulong OtherStranger = 0x7B4444444444;
    private const int Unknown = 15;

    private static readonly AppleModel AirPodsPro = AppleModels.Find(0x200E)!;
    private static readonly AppleModel AirPodsMax = AppleModels.Find(0x200A)!;

    private readonly ManualTimeProvider _time = new();

    private AppleBroadcastTracker Tracker(AppleModel? model = null) => new(model ?? AirPodsPro, _time);

    /// <summary>A status frame with levels in tenths (15 = not reported), sent by the right pod unless said otherwise.</summary>
    private static ProximityStatus Frame(int left, int right, int @case = Unknown, bool fromLeft = false,
        bool leftCharging = false, bool caseCharging = false) =>
        new(0x200E, fromLeft, fromLeft ? left : right, fromLeft ? right : left, @case,
            ThisCharging: fromLeft && leftCharging, OtherCharging: !fromLeft && leftCharging, CaseCharging: caseCharging,
            Status: (byte)(fromLeft ? 0x2B : 0x0B), Lid: 0x01, State: 0x05);

    private static void Twice(AppleBroadcastTracker tracker, ulong address, short rssi, ProximityStatus frame)
    {
        tracker.Offer(address, rssi, frame);
        tracker.Offer(address, rssi, frame);
    }

    private void Wait(double seconds) => _time.Advance(TimeSpan.FromSeconds(seconds));

    private static IReadOnlyList<BatteryComponent> Pods(int? left, int? right, int? @case = null) =>
    [
        new("Left", left, ChargeState.Unknown),
        new("Right", right, ChargeState.Unknown),
        new("Case", @case, ChargeState.Unknown),
    ];

    [Fact]
    public void Nothing_is_shown_while_the_device_is_not_connected()
    {
        var tracker = Tracker();

        Twice(tracker, Ours, -60, Frame(8, 9));

        Assert.Empty(tracker.Snapshot().Components);
        Assert.Null(tracker.Snapshot().Address);
    }

    [Fact]
    public void An_address_born_at_the_connect_is_chosen_after_two_frames()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());

        tracker.Offer(Ours, -70, Frame(8, 9));
        Assert.Empty(tracker.Snapshot().Components);

        tracker.Offer(Ours, -70, Frame(8, 9));
        var snapshot = tracker.Snapshot();

        Assert.Equal(Pods(80, 90), snapshot.Components);
        Assert.Equal(Ours, snapshot.Address);
        Assert.Equal(AppleBroadcastTracker.BornAtConnectRule, snapshot.Rule);
        Assert.Equal(_time.GetUtcNow(), snapshot.LastHeard);
    }

    [Fact]
    public void A_weak_address_born_at_the_connect_beats_a_stronger_stranger_heard_before_it()
    {
        var tracker = Tracker();
        Twice(tracker, Stranger, -45, Frame(3, 3));
        Wait(120);
        tracker.Connected(_time.GetUtcNow());
        Wait(2);

        Twice(tracker, Stranger, -45, Frame(3, 3));
        Twice(tracker, Ours, -88, Frame(8, 9));

        Assert.Equal(Ours, tracker.Snapshot().Address);
        Assert.Equal(Pods(80, 90), tracker.Snapshot().Components);
    }

    [Fact]
    public void Addresses_first_heard_up_to_a_minute_before_the_connect_count_as_born_at_it()
    {
        var tracker = Tracker();
        Twice(tracker, Ours, -85, Frame(8, 9)); // lid opened, pods taken out
        Wait(50);
        tracker.Connected(_time.GetUtcNow());

        Twice(tracker, Ours, -85, Frame(8, 9));

        Assert.Equal(AppleBroadcastTracker.BornAtConnectRule, tracker.Snapshot().Rule);
    }

    [Fact]
    public void Without_a_known_connect_moment_the_strongest_address_above_the_floor_is_chosen()
    {
        var tracker = Tracker();
        tracker.Connected(null);

        Twice(tracker, Stranger, -75, Frame(3, 3));
        Twice(tracker, Ours, -55, Frame(8, 9));
        Assert.Null(tracker.Snapshot().Address); // every address in range gets a few seconds to be heard first

        Wait(6);
        tracker.Offer(Stranger, -75, Frame(3, 3));

        Assert.Equal(Ours, tracker.Snapshot().Address);
        Assert.Equal(AppleBroadcastTracker.StrongestRule, tracker.Snapshot().Rule);
    }

    [Fact]
    public void Without_a_known_connect_moment_nothing_below_the_floor_is_chosen()
    {
        var tracker = Tracker();
        tracker.Connected(null);

        Twice(tracker, Ours, -90, Frame(8, 9));
        Wait(6);
        tracker.Offer(Ours, -90, Frame(8, 9));

        Assert.Null(tracker.Snapshot().Address);
        Assert.Empty(tracker.Snapshot().Components);
    }

    [Fact]
    public void After_a_known_connect_the_strongest_rule_waits_until_no_new_address_can_be_born()
    {
        var tracker = Tracker();
        Twice(tracker, Stranger, -50, Frame(3, 3));
        Wait(5 * 60);
        tracker.Connected(_time.GetUtcNow());

        Twice(tracker, Stranger, -50, Frame(3, 3));
        Assert.Null(tracker.Snapshot().Address);

        Wait(31);
        Twice(tracker, Stranger, -50, Frame(3, 3));

        Assert.Equal(Stranger, tracker.Snapshot().Address);
        Assert.Equal(AppleBroadcastTracker.StrongestRule, tracker.Snapshot().Rule);
    }

    [Fact]
    public void An_address_born_at_the_connect_takes_over_from_one_chosen_as_the_strongest()
    {
        var tracker = Tracker();
        Twice(tracker, Stranger, -50, Frame(3, 3, @case: 4));
        Wait(5 * 60);
        tracker.Connected(_time.GetUtcNow());
        Wait(10);
        tracker.Offer(Ours, -95, Frame(8, 9));
        Wait(21);
        Twice(tracker, Stranger, -50, Frame(3, 3, @case: 4));
        Assert.Equal(Stranger, tracker.Snapshot().Address);

        tracker.Offer(Ours, -95, Frame(8, 9));

        Assert.Equal(Ours, tracker.Snapshot().Address);
        Assert.Equal(AppleBroadcastTracker.BornAtConnectRule, tracker.Snapshot().Rule);
        Assert.Equal(Pods(80, 90), tracker.Snapshot().Components); // nothing of the stranger's is kept
    }

    [Fact]
    public void The_chosen_address_is_kept_however_weak_while_a_stronger_one_appears()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -80, Frame(8, 9));
        Wait(20);

        Twice(tracker, Stranger, -40, Frame(3, 3));
        tracker.Offer(Ours, -99, Frame(7, 9));

        Assert.Equal(Ours, tracker.Snapshot().Address);
        Assert.Equal(Pods(70, 90), tracker.Snapshot().Components);
    }

    [Fact]
    public void After_the_chosen_address_goes_silent_the_one_with_matching_levels_follows_it()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -70, Frame(8, 9));
        Wait(5 * 60);

        // The address rotated: our pods come back weaker than a stranger of the same model with other levels.
        Twice(tracker, Stranger, -45, Frame(3, 3));
        Twice(tracker, OurOtherPod, -85, Frame(8, 9, fromLeft: true));

        Assert.Equal(OurOtherPod, tracker.Snapshot().Address);
        Assert.Equal(AppleBroadcastTracker.ContinuityRule, tracker.Snapshot().Rule);
    }

    [Fact]
    public void Matching_allows_one_step_on_each_level()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -70, Frame(8, 9, @case: 5));
        Wait(31);

        Twice(tracker, Stranger, -50, Frame(5, 5));
        Twice(tracker, OurOtherPod, -85, Frame(7, 10, @case: Unknown));

        Assert.Equal(OurOtherPod, tracker.Snapshot().Address);
        Assert.Equal(Pods(70, 100, 50), tracker.Snapshot().Components);
    }

    [Fact]
    public void Two_steps_apart_is_no_match_so_the_strongest_is_taken()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -70, Frame(8, 9));
        Wait(2 * 60);

        Twice(tracker, OtherStranger, -60, Frame(6, 9));
        Twice(tracker, Stranger, -50, Frame(3, 3));
        Assert.Null(tracker.Snapshot().Address); // no match: the others get a few seconds to be heard

        Wait(11);
        tracker.Offer(Stranger, -50, Frame(3, 3));

        Assert.Equal(Stranger, tracker.Snapshot().Address);
        Assert.Equal(AppleBroadcastTracker.StrongestRule, tracker.Snapshot().Rule);
    }

    [Fact]
    public void When_two_addresses_match_the_stronger_wins()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -70, Frame(8, 9));
        Wait(20);
        Twice(tracker, Stranger, -80, Frame(8, 9));
        Twice(tracker, OurOtherPod, -60, Frame(8, 9, fromLeft: true));
        Wait(11);

        tracker.Offer(Stranger, -80, Frame(8, 9));

        Assert.Equal(OurOtherPod, tracker.Snapshot().Address);
    }

    [Fact]
    public void Nothing_of_a_stranger_picked_on_signal_alone_is_kept_once_it_goes_silent()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -70, Frame(8, 9, @case: 6));
        Wait(31);
        Twice(tracker, Stranger, -50, Frame(3, 3, @case: 4)); // ours went silent: the stranger is heard
        Wait(11);
        tracker.Offer(Stranger, -50, Frame(3, 3, @case: 4));
        Assert.Equal(AppleBroadcastTracker.StrongestRule, tracker.Snapshot().Rule);
        Wait(31);

        // Ours come back reporting the left pod only: they continue the levels shown before the stranger.
        Twice(tracker, OurOtherPod, -85, Frame(8, Unknown, fromLeft: true));

        Assert.Equal(OurOtherPod, tracker.Snapshot().Address);
        Assert.Equal(AppleBroadcastTracker.ContinuityRule, tracker.Snapshot().Rule);
        Assert.Equal(Pods(80, null), tracker.Snapshot().Components);
    }

    [Fact]
    public void A_level_not_reported_keeps_the_last_reported_one()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -70, Frame(8, 9, @case: 6));
        Wait(10);

        tracker.Offer(Ours, -70, Frame(8, Unknown, @case: Unknown));

        Assert.Equal(Pods(80, 90, 60), tracker.Snapshot().Components);
    }

    [Fact]
    public void Pod_levels_expire_after_three_minutes_and_the_case_after_ten()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -70, Frame(8, 9, @case: 6));

        Wait(3 * 60 - 1);
        Assert.Equal(Pods(80, 90, 60), tracker.Snapshot().Components);

        Wait(2);
        Assert.Equal(Pods(null, null, 60), tracker.Snapshot().Components);

        Wait(7 * 60);
        Assert.Empty(tracker.Snapshot().Components);
    }

    [Fact]
    public void A_fresh_frame_without_the_case_keeps_the_case_only_for_its_own_ten_minutes()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -70, Frame(8, 9, @case: 6));

        for (var i = 0; i < 11; i++)
        {
            Wait(60);
            tracker.Offer(Ours, -70, Frame(8, 9));
        }

        Assert.Equal(Pods(80, 90), tracker.Snapshot().Components);
    }

    [Fact]
    public void A_new_connect_forgets_the_choice_and_the_levels()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -70, Frame(8, 9));
        Wait(60);

        tracker.Connected(_time.GetUtcNow());

        Assert.Null(tracker.Snapshot().Address);
        Assert.Empty(tracker.Snapshot().Components);
        Assert.Null(tracker.Snapshot().LastHeard);
    }

    [Fact]
    public void A_disconnect_forgets_the_choice_and_the_levels()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());
        Twice(tracker, Ours, -70, Frame(8, 9));

        tracker.Disconnected();
        tracker.Offer(Ours, -70, Frame(8, 9));

        Assert.Empty(tracker.Snapshot().Components);
    }

    [Fact]
    public void Frames_without_a_signal_value_are_used_from_the_chosen_address_but_never_choose_one()
    {
        var tracker = Tracker();
        tracker.Connected(null);

        Twice(tracker, Stranger, AppleBroadcastTracker.NoSignal, Frame(3, 3));
        Assert.Null(tracker.Snapshot().Address);

        Twice(tracker, Ours, -60, Frame(8, 9));
        Wait(6);
        tracker.Offer(Ours, -60, Frame(8, 9));
        tracker.Offer(Ours, AppleBroadcastTracker.NoSignal, Frame(7, 9));

        Assert.Equal(Ours, tracker.Snapshot().Address);
        Assert.Equal(Pods(70, 90), tracker.Snapshot().Components);
    }

    [Fact]
    public void Charging_is_reported_for_the_pod_and_case_whose_bits_are_set()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());

        Twice(tracker, Ours, -70, Frame(8, 9, @case: 6, leftCharging: true, caseCharging: true));

        Assert.Equal(
            [
                new BatteryComponent("Left", 80, ChargeState.Charging),
                new BatteryComponent("Right", 90, ChargeState.Unknown),
                new BatteryComponent("Case", 60, ChargeState.Charging),
            ],
            tracker.Snapshot().Components);
    }

    [Fact]
    public void A_one_battery_charging_bit_is_held_for_thirty_seconds()
    {
        var tracker = Tracker(AirPodsMax);
        tracker.Connected(_time.GetUtcNow());
        var charging = new ProximityStatus(0x200A, false, 6, 0, 0, ThisCharging: true, false, false, 0x01, 0x00, 0x00);

        Twice(tracker, Ours, -60, charging);
        Wait(20);
        tracker.Offer(Ours, -60, charging with { ThisCharging = false });
        Assert.Equal([BatteryComponent.Single(60, ChargeState.Charging)], tracker.Snapshot().Components);

        Wait(11);
        tracker.Offer(Ours, -60, charging with { ThisCharging = false });
        Assert.Equal([BatteryComponent.Single(60, ChargeState.Unknown)], tracker.Snapshot().Components);
    }

    [Fact]
    public void Offer_says_when_the_shown_levels_change()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());

        Assert.False(tracker.Offer(Ours, -70, Frame(8, 9)));
        Assert.True(tracker.Offer(Ours, -70, Frame(8, 9)));
        Assert.False(tracker.Offer(Ours, -70, Frame(8, 9)));
        Assert.True(tracker.Offer(Ours, -70, Frame(7, 9)));
        Assert.False(tracker.Offer(Stranger, -40, Frame(3, 3)));
    }

    [Fact]
    public void Frames_of_another_model_are_ignored()
    {
        var tracker = Tracker();
        tracker.Connected(_time.GetUtcNow());

        Twice(tracker, Ours, -60, Frame(8, 9) with { ModelId = 0x2014 });

        Assert.Null(tracker.Snapshot().Address);
    }
}
