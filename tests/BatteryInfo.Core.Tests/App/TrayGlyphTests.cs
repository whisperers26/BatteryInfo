using BatteryInfo.App.Tray;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public class TrayGlyphTests
{
    [Theory]
    [InlineData(100, false, 0xE83F)] // Battery10
    [InlineData(96, false, 0xE83F)]
    [InlineData(64, false, 0xE856)] // Battery6
    [InlineData(4, false, 0xE850)] // Battery0
    [InlineData(0, false, 0xE850)]
    [InlineData(100, true, 0xE83E)] // BatteryCharging10
    [InlineData(51, true, 0xE85F)] // BatteryCharging5
    [InlineData(0, true, 0xE85A)] // BatteryCharging0
    public void A_level_picks_the_battery_glyph_filled_to_the_nearest_tenth(int percent, bool charging, int glyph)
    {
        Assert.Equal(((char)glyph).ToString(), TrayIconRenderer.GlyphFor(new TrayLevel(percent, charging)));
    }

    [Fact]
    public void No_level_is_the_full_battery()
    {
        Assert.Equal(TrayIconRenderer.BatteryGlyph, TrayIconRenderer.GlyphFor(null));
    }
}
