namespace BatteryInfo.Core.Settings;

/// <summary>The user's settings, stored in settings.json.</summary>
/// <param name="PollIntervalSeconds">How often every provider is polled.</param>
/// <param name="LowBatteryThreshold">A component at or below this percent, not charging, raises a toast.</param>
/// <param name="NotificationsEnabled">Whether low-battery toasts are shown.</param>
/// <param name="TraceFrames">Whether HID/SPP frames are written to the log.</param>
/// <param name="AutoUpdate">Whether the app checks for, and downloads, updates by itself.</param>
public sealed record AppSettings(
    int PollIntervalSeconds = 120,
    int LowBatteryThreshold = 20,
    bool NotificationsEnabled = true,
    bool TraceFrames = false,
    bool AutoUpdate = true)
{
    public const int MinPollIntervalSeconds = 10;
    public const int MaxPollIntervalSeconds = 24 * 60 * 60;

    /// <summary>These settings with every out-of-range value replaced by its default.</summary>
    public AppSettings Normalized()
    {
        var defaults = new AppSettings();
        return this with
        {
            PollIntervalSeconds = PollIntervalSeconds is >= MinPollIntervalSeconds and <= MaxPollIntervalSeconds
                ? PollIntervalSeconds
                : defaults.PollIntervalSeconds,
            LowBatteryThreshold = LowBatteryThreshold is >= 1 and <= 100 ? LowBatteryThreshold : defaults.LowBatteryThreshold,
        };
    }
}
