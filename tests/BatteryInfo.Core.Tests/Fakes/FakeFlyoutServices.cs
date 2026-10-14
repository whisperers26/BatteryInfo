using BatteryInfo.App.ViewModels;
using BatteryInfo.Core.History;

namespace BatteryInfo.Core.Tests.Fakes;

/// <summary>Flyout services that do nothing but remember the device order and tray setting they were given: for view-model tests.</summary>
internal sealed class FakeFlyoutServices : IFlyoutServices
{
    public DateTimeOffset Now => DateTimeOffset.UnixEpoch;

    public Task RefreshAsync() => Task.CompletedTask;

    public IReadOnlyList<HistorySeries> LoadHistory(string deviceId, DateTimeOffset since) => [];

    public bool IsStartWithWindowsEnabled() => false;

    public void SetStartWithWindows(bool enabled)
    {
    }

    public void SetLowBatteryThreshold(int percent)
    {
    }

    public void SetNotificationsEnabled(bool enabled)
    {
    }

    public void SetAutoUpdate(bool enabled)
    {
    }

    public bool? TrayShowsLevel { get; private set; }

    public void SetTrayShowsLevel(bool enabled) => TrayShowsLevel = enabled;

    public void RunUpdateAction()
    {
    }

    public IReadOnlyList<string>? DeviceOrder { get; private set; }

    public void SetDeviceOrder(IReadOnlyList<string> shown) => DeviceOrder = shown;
}
