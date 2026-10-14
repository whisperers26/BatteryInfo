namespace BatteryInfo.App;

/// <param name="Tray">Started by Windows at sign-in (the autostart value passes --tray): stay quiet in the tray.</param>
/// <param name="Demo">Show sample devices instead of real hardware, with their own data folder.</param>
/// <param name="ShowFlyout">Open the flyout right away (for testing and screenshots).</param>
internal sealed record StartupOptions(bool Tray, bool Demo, bool ShowFlyout)
{
    public static StartupOptions Parse(IEnumerable<string> args)
    {
        var set = new HashSet<string>(args, StringComparer.OrdinalIgnoreCase);
        return new StartupOptions(set.Contains("--tray"), set.Contains("--demo"), set.Contains("--show-flyout"));
    }
}
