namespace BatteryInfo.Core.Presentation;

/// <summary>The screen edge the taskbar is docked to, in the order of the ABE_* values of SHAppBarMessage.</summary>
public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <summary>A rectangle in physical screen pixels (right and bottom exclusive, like a Win32 RECT).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

public readonly record struct PixelPoint(int X, int Y);

/// <summary>Where the flyout goes: in the corner nearest the notification area, next to the taskbar, like the
/// Windows 11 quick settings.</summary>
public static class FlyoutPlacement
{
    /// <param name="taskbar">The taskbar's rectangle (ABM_GETTASKBARPOS); kept clear even when it auto-hides
    /// over the work area.</param>
    /// <param name="workArea">The work area of the taskbar's monitor.</param>
    /// <returns>The flyout's top-left corner in physical pixels.</returns>
    public static PixelPoint Place(TaskbarEdge edge, PixelRect taskbar, PixelRect workArea, int width, int height, int margin)
    {
        var left = workArea.Left;
        var top = workArea.Top;
        var right = workArea.Right;
        var bottom = workArea.Bottom;
        switch (edge)
        {
            case TaskbarEdge.Left:
                left = Math.Max(left, taskbar.Right);
                break;
            case TaskbarEdge.Top:
                top = Math.Max(top, taskbar.Bottom);
                break;
            case TaskbarEdge.Right:
                right = Math.Min(right, taskbar.Left);
                break;
            default:
                bottom = Math.Min(bottom, taskbar.Top);
                break;
        }

        var x = edge == TaskbarEdge.Left ? left + margin : right - width - margin;
        var y = edge == TaskbarEdge.Top ? top + margin : bottom - height - margin;
        return new PixelPoint(Math.Max(x, workArea.Left), Math.Max(y, workArea.Top));
    }
}
