using BatteryInfo.Core.Presentation;
using BatteryInfo.Core.Updates;

namespace BatteryInfo.Core.Tests.App;

public class UpdateTextTests
{
    [Theory]
    [InlineData(UpdateState.Idle, null, null, "BatteryInfo 1.0.0", "Check for updates", true)]
    [InlineData(UpdateState.Checking, null, null, "Checking for updates…", "Check for updates", false)]
    [InlineData(UpdateState.Downloading, "1.1.0", null, "Downloading 1.1.0…", "Check for updates", false)]
    [InlineData(UpdateState.UpToDate, null, null, "BatteryInfo 1.0.0 is up to date", "Check for updates", true)]
    [InlineData(UpdateState.ReadyToRestart, "1.1.0", null, "Version 1.1.0 is ready to install", "Restart to update", true)]
    [InlineData(UpdateState.Failed, null, "No connection", "Update check failed: No connection", "Try again", true)]
    public void Describes_each_state(UpdateState state, string? newVersion, string? message, string line, string action, bool enabled)
    {
        var text = UpdateText.Describe(new UpdateStatus(state, "1.0.0", newVersion, message));

        Assert.Equal(line, text.StatusLine);
        Assert.Equal(action, text.ActionLabel);
        Assert.Equal(enabled, text.ActionEnabled);
    }

    [Fact]
    public void Unavailable_shows_the_reason_and_no_action()
    {
        var text = UpdateText.Describe(new UpdateStatus(UpdateState.Unavailable, null, Message: "Updates work only in the installed app."));

        Assert.Equal("Updates work only in the installed app.", text.StatusLine);
        Assert.False(text.ActionEnabled);
    }

    [Fact]
    public void Missing_version_reads_as_a_local_build()
    {
        Assert.Equal("BatteryInfo (local build)", UpdateText.Describe(new UpdateStatus(UpdateState.Idle, null)).StatusLine);
    }

    [Fact]
    public void Tray_menu_offers_restart_only_when_ready()
    {
        Assert.Equal("Check for updates", UpdateText.MenuLabel(new UpdateStatus(UpdateState.UpToDate, "1.0.0")));
        Assert.Equal("Restart to update to 1.1.0", UpdateText.MenuLabel(new UpdateStatus(UpdateState.ReadyToRestart, "1.0.0", "1.1.0")));
    }

    [Fact]
    public void Ready_notification_says_when_it_installs()
    {
        Assert.Equal("BatteryInfo 1.1.0 is ready. It installs the next time BatteryInfo starts, or choose Restart to update now.",
            UpdateText.ReadyMessage("1.1.0"));
    }
}
