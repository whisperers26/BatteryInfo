using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BatteryInfo.App.Interop;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.App.Flyout;

/// <summary>Puts the flyout next to the notification area: finds the taskbar (SHAppBarMessage), its monitor's
/// work area and DPI, and moves the window there in physical pixels.</summary>
internal static class FlyoutPositioner
{
    private const double MarginDip = 12;

    public static void Position(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var (edge, taskbar) = FindTaskbar();
        var bounds = ToNative(taskbar);
        var monitor = NativeMethods.MonitorFromRect(ref bounds, NativeMethods.MonitorDefaultToPrimary);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;
        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        var work = FromNative(info.Work);

        // WPF sizes are in DIPs of the monitor the window ends up on.
        window.MaxHeight = Math.Max(200, work.Height / scale - 2 * MarginDip);
        var heightDip = window.IsVisible ? window.ActualHeight : MeasureHeight(window);
        var position = FlyoutPlacement.Place(edge, taskbar, work,
            (int)Math.Ceiling(window.Width * scale), (int)Math.Ceiling(heightDip * scale), (int)Math.Round(MarginDip * scale));
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, position.X, position.Y, 0, 0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    private static double MeasureHeight(Window window)
    {
        if (window.Content is not UIElement content) return window.MinHeight;
        content.Measure(new Size(window.Width, double.PositiveInfinity));
        return Math.Min(content.DesiredSize.Height, window.MaxHeight);
    }

    private static (TaskbarEdge Edge, PixelRect Bounds) FindTaskbar()
    {
        var data = new NativeMethods.AppBarData { Size = (uint)Marshal.SizeOf<NativeMethods.AppBarData>() };
        if (NativeMethods.SHAppBarMessage(NativeMethods.AbmGetTaskbarPos, ref data) != UIntPtr.Zero && data.Edge <= 3)
        {
            return ((TaskbarEdge)data.Edge, FromNative(data.Bounds));
        }

        // No taskbar (Explorer restarting): assume a bottom taskbar on the monitor under the cursor.
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        var work = FromNative(info.Work);
        return (TaskbarEdge.Bottom, work with { Top = work.Bottom });
    }

    private static PixelRect FromNative(NativeMethods.Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private static NativeMethods.Rect ToNative(PixelRect r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
}
