using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatteryInfo.App.Interop;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.App.Tray;

/// <summary>Draws a Segoe Fluent Icons glyph into a tray icon, white for a dark taskbar and black for a light one.</summary>
internal static class TrayIconRenderer
{
    /// <summary>Battery (full) in Segoe Fluent Icons / Segoe MDL2 Assets.</summary>
    public const string BatteryGlyph = "";

    /// <summary>The glyph for a level: Battery0-Battery10 or BatteryCharging0-BatteryCharging10, filled to the nearest
    /// tenth. No level gives the full battery.</summary>
    public static string GlyphFor(TrayLevel? level)
    {
        if (level is null) return BatteryGlyph;
        var tenths = (int)Math.Round(Math.Clamp(level.Percent, 0, 100) / 10.0, MidpointRounding.AwayFromZero);
        // Battery0-9 are E850-E859 and Battery10 is E83F. BatteryCharging0-8 are E85A-E862, but BatteryCharging9 and 10
        // are E83E and EA93: E863 after them is BatterySaver0, an empty battery.
        var glyph = level.Charging
            ? tenths switch { 10 => 0xEA93, 9 => 0xE83E, _ => 0xE85A + tenths }
            : tenths == 10 ? 0xE83F : 0xE850 + tenths;
        return ((char)glyph).ToString();
    }

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>The small-icon size the notification area uses now (16 px at 100 %): at the current DPI of the
    /// taskbar's monitor. The system DPI would not do: it is fixed for the life of the process, so it misses a
    /// change of the display scale.</summary>
    public static int TraySize()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        var monitor = taskbar != IntPtr.Zero
            ? NativeMethods.MonitorFromWindow(taskbar, NativeMethods.MonitorDefaultToPrimary)
            : NativeMethods.MonitorFromPoint(default, NativeMethods.MonitorDefaultToPrimary);
        var dpi = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _) == 0
            ? dpiX
            : NativeMethods.GetDpiForSystem();
        var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSmIcon, dpi);
        return size > 0 ? size : 16;
    }

    public static System.Drawing.Icon Render(string glyph, bool lightTaskbar, int size)
    {
        var pixels = RenderPixels(glyph, lightTaskbar ? Colors.Black : Colors.White, size);
        using var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, size, size),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            for (var row = 0; row < size; row++)
            {
                Marshal.Copy(pixels, row * size * 4, data.Scan0 + row * data.Stride, size * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)borrowed.Clone(); // the clone owns a copy of the handle
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    // WPF renders the glyph (premultiplied BGRA) at the font's own size, where its strokes sit on whole pixels
    // (Segoe Fluent Icons is drawn on a 1/16 em grid), centred by a whole-pixel offset so they stay sharp.
    private static byte[] RenderPixels(string glyph, Color color, int size)
    {
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size,
            new SolidColorBrush(color), 1.0);
        var geometry = text.BuildGeometry(new Point(0, 0));
        var ink = geometry.Bounds;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (!ink.IsEmpty && ink.Width > 0 && ink.Height > 0)
            {
                var scale = Math.Min(1.0, Math.Min(size / ink.Width, size / ink.Height));
                dc.PushTransform(new TranslateTransform(Math.Round((size - ink.Width * scale) / 2), Math.Round((size - ink.Height * scale) / 2)));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.PushTransform(new TranslateTransform(-ink.X, -ink.Y));
                dc.DrawGeometry(new SolidColorBrush(color), null, geometry);
            }
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        return pixels;
    }
}
