using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public class RelativeTimeTests
{
    private static readonly TimeSpan Beijing = TimeSpan.FromHours(8);
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 15, 30, 0, Beijing);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(0.5, "just now")]
    [InlineData(1, "1 min ago")]
    [InlineData(14, "14 min ago")]
    [InlineData(59.9, "59 min ago")]
    [InlineData(60, "1 h ago")]
    [InlineData(15 * 60, "15 h ago")]
    public void Same_day(double minutesAgo, string expected)
    {
        Assert.Equal(expected, RelativeTime.Format(Now.AddMinutes(-minutesAgo), Now));
    }

    [Fact]
    public void Previous_calendar_day_is_yesterday_even_when_less_than_a_day_ago()
    {
        Assert.Equal("yesterday", RelativeTime.Format(new DateTimeOffset(2026, 10, 13, 23, 50, 0, Beijing), Now));
        Assert.Equal("yesterday", RelativeTime.Format(new DateTimeOffset(2026, 10, 13, 0, 10, 0, Beijing), Now));
    }

    [Fact]
    public void Days_and_dates_further_back()
    {
        Assert.Equal("3 days ago", RelativeTime.Format(new DateTimeOffset(2026, 10, 11, 9, 0, 0, Beijing), Now));
        Assert.Equal("6 days ago", RelativeTime.Format(new DateTimeOffset(2026, 10, 8, 9, 0, 0, Beijing), Now));
        Assert.Equal("on 2 Oct", RelativeTime.Format(new DateTimeOffset(2026, 10, 2, 9, 0, 0, Beijing), Now));
    }

    [Fact]
    public void Calendar_days_are_counted_in_the_time_zone_of_now()
    {
        // 2026-10-13 18:00 UTC is 2026-10-14 02:00 in UTC+8: the same day there.
        var then = new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.Zero);

        Assert.Equal("13 h ago", RelativeTime.Format(then, Now));
    }

    [Fact]
    public void Times_in_the_future_are_just_now()
    {
        Assert.Equal("just now", RelativeTime.Format(Now.AddMinutes(3), Now));
    }
}
