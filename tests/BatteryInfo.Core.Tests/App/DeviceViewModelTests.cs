using BatteryInfo.App.ViewModels;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Tests.App;

public class DeviceViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 14, 0, 0, TimeSpan.FromHours(-5));

    private static DeviceReading AirPods(DeviceStatus status, IReadOnlyList<BatteryComponent> components, DateTimeOffset at,
        string? detail = null) =>
        new("apple:9CA9C520AFB8", "Apple", "AirPods 4", DeviceKind.Earbuds, status, components, at,
            DeviceReading.AppleBroadcastSource, Approximate: components.Count > 0, Detail: detail);

    private static readonly IReadOnlyList<BatteryComponent> Levels =
    [
        new("Left", 10, ChargeState.Unknown),
        new("Right", 10, ChargeState.Unknown),
        new("Case", 60, ChargeState.Unknown),
    ];

    private static DeviceViewModel Row(DeviceState state)
    {
        var row = new DeviceViewModel(state.Latest.DeviceId, _ => { });
        row.Update(state, Now, lowThreshold: 20);
        return row;
    }

    [Fact]
    public void A_row_with_live_values_is_not_dimmed() =>
        Assert.False(Row(new DeviceState(AirPods(DeviceStatus.Online, Levels, Now), AirPods(DeviceStatus.Online, Levels, Now))).IsDimmed);

    [Fact]
    public void A_connected_row_showing_an_older_reading_s_values_is_dimmed()
    {
        // Connected again after charging in the case, before the first broadcast: the levels shown are the last session's.
        var waiting = AirPods(DeviceStatus.Online, [], Now, "Waiting for its Bluetooth battery broadcast");
        var lastSession = AirPods(DeviceStatus.Online, Levels, Now.AddHours(-3));

        var row = Row(new DeviceState(waiting, lastSession));

        Assert.True(row.IsDimmed);
        Assert.Equal("Waiting for its Bluetooth battery broadcast", row.StatusText);
    }

    [Fact]
    public void An_asleep_row_is_dimmed() =>
        Assert.True(Row(new DeviceState(AirPods(DeviceStatus.Asleep, [], Now), AirPods(DeviceStatus.Online, Levels, Now.AddHours(-1)))).IsDimmed);
}
