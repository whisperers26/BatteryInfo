using System.Globalization;

namespace BatteryInfo.Core.Presentation;

/// <summary>Short English phrases for how long ago something happened: "just now", "14 min ago", "3 h ago",
/// "yesterday", "3 days ago", "on 2 Oct".</summary>
public static class RelativeTime
{
    /// <param name="now">The current local time; calendar days are counted in its offset.</param>
    public static string Format(DateTimeOffset then, DateTimeOffset now)
    {
        var age = now - then;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";

        var days = (now.Date - then.ToOffset(now.Offset).Date).Days;
        return days switch
        {
            0 => $"{(int)age.TotalHours} h ago",
            1 => "yesterday",
            < 7 => $"{days} days ago",
            _ => "on " + then.ToOffset(now.Offset).ToString("d MMM", CultureInfo.InvariantCulture),
        };
    }
}
