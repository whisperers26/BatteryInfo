using BatteryInfo.Core.Vendors.Apple;
using static BatteryInfo.Core.Tests.Vendors.Apple.AppleVectors;

namespace BatteryInfo.Core.Tests.Vendors.Apple;

public class AppleProximityProtocolTests
{
    private static ProximityStatus Parse(string hex) =>
        AppleProximityProtocol.Parse(Bytes(hex)) ?? throw new Xunit.Sdk.XunitException($"not parsed: {hex}");

    [Fact]
    public void Reads_every_field_of_the_frame_apple_decoded_itself()
    {
        // iOS: "This: Right - Primary - In Case", "My Batt: +95, Oth Batt: invalid, C Batt: -97", lid open count 1.
        var status = Parse(EarbudFrames[0].Hex);

        Assert.Equal(0x200E, status.ModelId);
        Assert.False(status.FromLeft);
        Assert.Equal(9, status.ThisLevel);
        Assert.Equal(15, status.OtherLevel);
        Assert.Equal(9, status.CaseLevel);
        Assert.True(status.ThisCharging);
        Assert.False(status.OtherCharging);
        Assert.False(status.CaseCharging);
        Assert.Equal(0x04, status.Status);
        Assert.Equal(0x31, status.Lid);
        Assert.Equal(0x00, status.State);
    }

    [Theory]
    [MemberData(nameof(EarbudNames), MemberType = typeof(AppleVectors))]
    public void Earbud_frames_give_the_levels_and_charging_their_source_reports(string name)
    {
        var frame = EarbudFrames.Single(f => f.Name == name);

        var status = Parse(frame.Hex);

        Assert.Equal(frame.ModelId, status.ModelId);
        Assert.Equal(frame.Left, AppleProximityProtocol.Percent(status.LeftLevel));
        Assert.Equal(frame.Right, AppleProximityProtocol.Percent(status.RightLevel));
        Assert.Equal(frame.Case, AppleProximityProtocol.Percent(status.CaseLevel));
        Assert.Equal(frame.LeftCharging, status.LeftCharging && frame.Left is not null);
        Assert.Equal(frame.RightCharging, status.RightCharging && frame.Right is not null);
        Assert.Equal(frame.CaseCharging, status.CaseCharging && frame.Case is not null);
    }

    [Fact]
    public void Both_pods_of_one_moment_decode_to_the_same_left_and_right()
    {
        // ha-airpods-bridge: the right pod sent 13 A9, the left one 73 9A; left 100 / right 98 confirmed over AAP.
        var right = Parse(EarbudFrames.Single(f => f.Name == "HaBridge_AirPodsPro_RightPodSends_UnequalAapConfirmed").Hex);
        var left = Parse(EarbudFrames.Single(f => f.Name == "HaBridge_AirPodsPro_LeftPodSends_UnequalAapConfirmed").Hex);

        Assert.False(right.FromLeft);
        Assert.True(left.FromLeft);
        Assert.Equal((10, 9), (right.LeftLevel, right.RightLevel));
        Assert.Equal((10, 9), (left.LeftLevel, left.RightLevel));
        Assert.True(right.LeftCharging && left.LeftCharging);
        Assert.False(right.RightCharging || left.RightCharging);
    }

    [Theory]
    [MemberData(nameof(OneBatteryNames), MemberType = typeof(AppleVectors))]
    public void One_battery_frames_carry_the_level_in_the_low_nibble(string name)
    {
        var frame = OneBatteryFrames.Single(f => f.Name == name);

        var status = Parse(frame.Hex);

        Assert.Equal(frame.ModelId, status.ModelId);
        Assert.Equal(frame.Level, AppleProximityProtocol.Percent(status.ThisLevel));
    }

    [Theory]
    [MemberData(nameof(RejectedNames), MemberType = typeof(AppleVectors))]
    public void Frames_that_are_not_a_whole_status_message_are_rejected(string name) =>
        Assert.Null(AppleProximityProtocol.Parse(Bytes(RejectedFrames.Single(f => f.Name == name).Hex)));

    [Fact]
    public void Finds_a_status_message_behind_another_message_in_the_same_entry()
    {
        var data = Bytes("10 06 0C 19 0B 3D 57 68 " + EarbudFrames[0].Hex);

        Assert.Equal((ushort)0x200E, AppleProximityProtocol.Parse(data)?.ModelId);
    }

    [Fact]
    public void A_message_running_past_the_end_stops_the_walk()
    {
        // The second message claims 0x40 bytes; the status message after it can no longer be trusted to start there.
        var data = Bytes("10 40 0C 19 " + EarbudFrames[0].Hex);

        Assert.Null(AppleProximityProtocol.Parse(data));
    }

    [Fact]
    public void Empty_data_is_rejected() => Assert.Null(AppleProximityProtocol.Parse([]));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(9, 90)]
    [InlineData(10, 100)]
    [InlineData(11, null)]
    [InlineData(14, null)]
    [InlineData(15, null)]
    public void Levels_are_tenths_and_anything_above_ten_is_not_reported(int nibble, int? percent) =>
        Assert.Equal(percent, AppleProximityProtocol.Percent(nibble));
}
