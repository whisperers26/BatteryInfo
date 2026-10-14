using BatteryInfo.Core.History;

namespace BatteryInfo.App.ViewModels;

/// <summary>What the flyout's view models ask of the app: refresh, history, settings and autostart.</summary>
public interface IFlyoutServices
{
    /// <summary>The current local time.</summary>
    DateTimeOffset Now { get; }

    Task RefreshAsync();

    /// <summary>Reads one device's chart series from disk. Called on a thread-pool thread.</summary>
    IReadOnlyList<HistorySeries> LoadHistory(string deviceId, DateTimeOffset since);

    bool IsStartWithWindowsEnabled();

    void SetStartWithWindows(bool enabled);

    void SetLowBatteryThreshold(int percent);

    void SetNotificationsEnabled(bool enabled);

    void SetAutoUpdate(bool enabled);

    /// <summary>Whether the tray icon shows the first device's level.</summary>
    void SetTrayShowsLevel(bool enabled);

    /// <summary>The shown devices were dragged into this order.</summary>
    void SetDeviceOrder(IReadOnlyList<string> shown);

    /// <summary>Checks for an update now, or restarts to install one already downloaded.</summary>
    void RunUpdateAction();
}
