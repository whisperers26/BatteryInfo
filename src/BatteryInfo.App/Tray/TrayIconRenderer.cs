using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatteryInfo.App.Interop;

namespace BatteryInfo.App.Tray;

/// <summary>Draws a Segoe Fluent Icons glyph into a tray icon, white for a dark taskbar and black for a light one.</summary>
internal static class TrayIconRenderer
{
    /// <summary>Battery (full) in Segoe Fluent Icons / Segoe MDL2 Assets.</summary>
    public const string BatteryGlyph = "";

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>The small-icon size the notification area uses at the system DPI (16 px at 100 %).</summary>
    public static int TraySize()
    {
        var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSmIcon, NativeMethods.GetDpiForSystem());
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
