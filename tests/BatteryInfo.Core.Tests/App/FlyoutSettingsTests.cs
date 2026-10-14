using BatteryInfo.App.ViewModels;
using BatteryInfo.Core.Settings;
using BatteryInfo.Core.Tests.Fakes;

namespace BatteryInfo.Core.Tests.App;

public class FlyoutSettingsTests
{
    private static FlyoutViewModel Flyout() => new(new FakeFlyoutServices(), new AppSettings());

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
