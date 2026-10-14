using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public class FlyoutPlacementTests
{
    // A 1920x1080 screen with a 48 px taskbar, flyout 540x600 physical pixels (360 DIP at 150 %), 18 px margin.
    private const int Width = 540;
    private const int Height = 600;
    private const int Margin = 18;

    [Fact]
    public void Bottom_taskbar_puts_the_flyout_above_the_tray_corner()
    {
        var position = FlyoutPlacement.Place(TaskbarEdge.Bottom, new PixelRect(0, 1032, 1920, 1080),
            new PixelRect(0, 0, 1920, 1032), Width, Height, Margin);

        Assert.Equal(new PixelPoint(1920 - 540 - 18, 1032 - 600 - 18), position);
    }

    [Fact]
    public void Top_taskbar_puts_it_below_the_taskbar_on_the_right()
    {
        var position = FlyoutPlacement.Place(TaskbarEdge.Top, new PixelRect(0, 0, 1920, 48),
            new PixelRect(0, 48, 1920, 1080), Width, Height, Margin);

        Assert.Equal(new PixelPoint(1362, 48 + 18), position);
    }

    [Fact]
    public void Left_taskbar_puts_it_in_the_bottom_left_corner_next_to_the_taskbar()
    {
        var position = FlyoutPlacement.Place(TaskbarEdge.Left, new PixelRect(0, 0, 62, 1080),
            new PixelRect(62, 0, 1920, 1080), Width, Height, Margin);

        Assert.Equal(new PixelPoint(62 + 18, 1080 - 600 - 18), position);
    }

    [Fact]
    public void Right_taskbar_puts_it_in_the_bottom_right_corner_next_to_the_taskbar()
    {
        var position = FlyoutPlacement.Place(TaskbarEdge.Right, new PixelRect(1858, 0, 1920, 1080),
            new PixelRect(0, 0, 1858, 1080), Width, Height, Margin);

        Assert.Equal(new PixelPoint(1858 - 540 - 18, 1080 - 600 - 18), position);
    }

    [Fact]
    public void An_auto_hide_taskbar_inside_the_work_area_is_still_kept_clear()
    {
        var position = FlyoutPlacement.Place(TaskbarEdge.Bottom, new PixelRect(0, 1032, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080), Width, Height, Margin);

        Assert.Equal(new PixelPoint(1362, 1032 - 600 - 18), position);
    }

    [Fact]
    public void A_flyout_taller_than_the_screen_is_kept_inside_the_work_area()
    {
        var position = FlyoutPlacement.Place(TaskbarEdge.Bottom, new PixelRect(0, 1032, 1920, 1080),
            new PixelRect(0, 0, 1920, 1032), Width, 2000, Margin);

        Assert.Equal(new PixelPoint(1362, 0), position);
    }

    [Fact]
    public void A_monitor_left_of_the_primary_uses_negative_coordinates()
    {
        var position = FlyoutPlacement.Place(TaskbarEdge.Bottom, new PixelRect(-1920, 1032, 0, 1080),
            new PixelRect(-1920, 0, 0, 1032), Width, Height, Margin);

        Assert.Equal(new PixelPoint(-540 - 18, 414), position);
    }
}
