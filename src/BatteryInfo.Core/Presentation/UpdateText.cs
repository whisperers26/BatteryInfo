using BatteryInfo.Core.Updates;

namespace BatteryInfo.Core.Presentation;

/// <summary>The flyout's update line and button for one update status.</summary>
public sealed record UpdateDescription(string StatusLine, string ActionLabel, bool ActionEnabled);

/// <summary>Words for the update status in the flyout, the tray menu and the "update ready" toast.</summary>
public static class UpdateText
{
    public const string CheckLabel = "Check for updates";
    public const string RestartLabel = "Restart to update";
    public const string ReadyTitle = "Update ready";

    public static UpdateDescription Describe(UpdateStatus status)
    {
        var current = status.CurrentVersion is { } v ? $"BatteryInfo {v}" : "BatteryInfo (local build)";
        return status.State switch
        {
            UpdateState.Checking => new("Checking for updates…", CheckLabel, false),
            UpdateState.Downloading => new($"Downloading {status.NewVersion}…", CheckLabel, false),
            UpdateState.UpToDate => new($"{current} is up to date", CheckLabel, true),
            UpdateState.ReadyToRestart => new($"Version {status.NewVersion} is ready to install", RestartLabel, true),
            UpdateState.Unavailable => new(status.Message ?? current, CheckLabel, false),
            UpdateState.Failed => new($"Update check failed: {status.Message}", "Try again", true),
            _ => new(current, CheckLabel, true),
        };
    }

    public static string MenuLabel(UpdateStatus status) =>
        status.State == UpdateState.ReadyToRestart ? $"{RestartLabel} to {status.NewVersion}" : CheckLabel;

    public static string ReadyMessage(string version) =>
        $"BatteryInfo {version} is ready. It installs the next time BatteryInfo starts, or choose {RestartLabel} now.";
}
