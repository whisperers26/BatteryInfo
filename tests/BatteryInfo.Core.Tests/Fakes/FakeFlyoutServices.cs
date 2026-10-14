using BatteryInfo.App.ViewModels;
using BatteryInfo.Core.History;

namespace BatteryInfo.Core.Tests.Fakes;

/// <summary>Flyout services that do nothing: for view-model tests.</summary>
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

    public void RunUpdateAction()
    {
    }
}
