namespace BatteryInfo.App;

/// <param name="Tray">Started by Windows at sign-in (the autostart value passes --tray): stay quiet in the tray.</param>
/// <param name="Demo">Show sample devices instead of real hardware, with their own data folder.</param>
/// <param name="ShowFlyout">Open the flyout right away (for testing and screenshots).</param>
/// <param name="Theme">"light" or "dark" (--theme=light) overrides the Windows app mode; null follows it.</param>
internal sealed record StartupOptions(bool Tray, bool Demo, bool ShowFlyout, string? Theme = null)
{
    private const string ThemePrefix = "--theme=";

    public static StartupOptions Parse(IEnumerable<string> args)
    {
        var list = args.ToList();
        var set = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        var theme = list.FirstOrDefault(a => a.StartsWith(ThemePrefix, StringComparison.OrdinalIgnoreCase))?[ThemePrefix.Length..]
            .ToLowerInvariant();
        return new StartupOptions(set.Contains("--tray"), set.Contains("--demo"), set.Contains("--show-flyout"),
            theme is "light" or "dark" ? theme : null);
    }
}
