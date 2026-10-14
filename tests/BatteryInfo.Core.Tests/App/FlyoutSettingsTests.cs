using BatteryInfo.App.ViewModels;
using BatteryInfo.Core.History;
using BatteryInfo.Core.Settings;

namespace BatteryInfo.Core.Tests.App;

public class FlyoutSettingsTests
{
    private sealed class Services : IFlyoutServices
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

    private static FlyoutViewModel Flyout() => new(new Services(), new AppSettings());

    [Fact]
    public void Settings_start_collapsed()
    {
        Assert.False(Flyout().SettingsExpanded);
    }

    [Fact]
    public void Settings_button_toggles_them()
    {
        var flyout = Flyout();

        flyout.ToggleSettingsCommand.Execute(null);
        Assert.True(flyout.SettingsExpanded);

        flyout.ToggleSettingsCommand.Execute(null);
        Assert.False(flyout.SettingsExpanded);
    }

    [Fact]
    public void Closing_the_flyout_collapses_the_settings()
    {
        var flyout = Flyout();
        flyout.IsOpen = true;
        flyout.ToggleSettingsCommand.Execute(null);

        flyout.IsOpen = false;
        flyout.IsOpen = true;

        Assert.False(flyout.SettingsExpanded);
    }

    [Fact]
    public void Expanding_notifies_the_view()
    {
        var flyout = Flyout();
        var changed = new List<string?>();
        flyout.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        flyout.ToggleSettingsCommand.Execute(null);

        Assert.Contains(nameof(FlyoutViewModel.SettingsExpanded), changed);
    }
}
