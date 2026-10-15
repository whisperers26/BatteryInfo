using System.Globalization;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Apple;

namespace BatteryInfo.Core.Tests.Vendors.Apple;

/// <summary>Replays the frames the owner's AirPods 4 (ANC) broadcast on this PC (Fixtures/airpods4-anc-live.txt) through
/// the tracker, at their real pace, as the app would have seen them after starting while the pods were connected.</summary>
public class AppleLiveCaptureTests
{
    private const ulong LeftPod = 0x5341481C2754;
    private const ulong RightPod = 0x4D00295F4A98;

    private sealed record Frame(double Seconds, ulong Address, short Rssi, ProximityStatus Status);

    private static IReadOnlyList<Frame> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Vendors", "Apple", "Fixtures", "airpods4-anc-live.txt");
        var frames = new List<Frame>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var parts = line.Split(' ', 4);
            var bytes = new byte[27]; // the tail was not logged: zeros, which the decoder never reads
            AppleVectors.Bytes(parts[3]).CopyTo(bytes, 0);
            frames.Add(new Frame(double.Parse(parts[0], CultureInfo.InvariantCulture), ulong.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                short.Parse(parts[2], CultureInfo.InvariantCulture), AppleProximityProtocol.Parse(bytes)!.Value));
        }
        return frames;
    }

    /// <summary>The snapshot after every frame, by the frame's time.</summary>
    private static List<(double Seconds, AppleBroadcastSnapshot Snapshot)> Replay()
    {
        var time = new ManualTimeProvider();
        var start = time.GetUtcNow();
        var tracker = new AppleBroadcastTracker(AppleModels.Find(0x201B)!, time);
        tracker.Connected(null);

        var shown = new List<(double, AppleBroadcastSnapshot)>();
        foreach (var frame in Load())
        {
            time.Advance(start + TimeSpan.FromSeconds(frame.Seconds) - time.GetUtcNow());
            tracker.Offer(frame.Address, frame.Rssi, frame.Status);
            shown.Add((frame.Seconds, tracker.Snapshot()));
        }
        return shown;
    }

    private static IReadOnlyList<BatteryComponent> At(List<(double Seconds, AppleBroadcastSnapshot Snapshot)> shown, double seconds) =>
        shown.Last(s => s.Seconds <= seconds).Snapshot.Components;

    [Fact]
    public void The_capture_holds_both_pods_in_the_case_then_the_left_pod_worn()
    {
        var frames = Load();

        Assert.Equal(488, frames.Count);
        Assert.All(frames, f => Assert.Equal(0x201B, f.Status.ModelId));
        Assert.Equal([LeftPod, RightPod], frames.Select(f => f.Address).Distinct().OrderDescending());
        Assert.True(frames.Where(f => f.Address == LeftPod).All(f => f.Status.FromLeft));
        Assert.True(frames.Where(f => f.Address == RightPod).All(f => !f.Status.FromLeft));
    }

    [Fact]
    public void The_pods_are_followed_from_the_first_seconds_to_the_end_without_a_gap()
    {
        var shown = Replay();

        var chosen = shown.Where(s => s.Seconds > 6).ToList();
        Assert.All(chosen, s => Assert.Equal(LeftPod, s.Snapshot.Address));
        Assert.All(chosen, s => Assert.NotEmpty(s.Snapshot.Components));
        Assert.All(shown.Where(s => s.Seconds < 5), s => Assert.Null(s.Snapshot.Address));
    }

    [Fact]
    public void In_the_case_both_pods_and_the_case_charge_and_the_case_climbs_from_0_to_10()
    {
        var shown = Replay();

        Assert.Equal(
            [
                new BatteryComponent("Left", 90, ChargeState.Charging),
                new BatteryComponent("Right", 90, ChargeState.Charging),
                new BatteryComponent("Case", 0, ChargeState.Charging),
            ],
            At(shown, 40));
        Assert.Equal(new BatteryComponent("Case", 10, ChargeState.Charging), At(shown, 60)[2]);
    }

    [Fact]
    public void Worn_the_pods_drop_from_90_to_80_and_the_case_keeps_its_last_level()
    {
        var shown = Replay();

        Assert.Equal(
            [
                new BatteryComponent("Left", 90, ChargeState.Unknown),
                new BatteryComponent("Right", 90, ChargeState.Unknown),
                new BatteryComponent("Case", 10, ChargeState.Charging),
            ],
            At(shown, 400));
        Assert.Equal([80, 80, 10], At(shown, 600).Select(c => c.Percent));
    }
}
