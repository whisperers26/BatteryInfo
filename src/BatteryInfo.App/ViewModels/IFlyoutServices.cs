using BatteryInfo.Core.History;

namespace BatteryInfo.App.ViewModels;

/// <summary>What the flyout's view models ask of the app: refresh, history, settings and autostart.</summary>
public interface IFlyoutServices
{
    /// <summary>The current local time.</summary>
    DateTimeOffset Now { get; }

    Task RefreshAsync();

    IReadOnlyList<HistorySeries> LoadHistory(string deviceId, DateTimeOffset since);

    bool IsStartWithWindowsEnabled();

    void SetStartWithWindows(bool enabled);

    void SetLowBatteryThreshold(int percent);

    void SetNotificationsEnabled(bool enabled);
}
