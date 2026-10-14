using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BatteryInfo.App.Tray;

/// <summary>Draws the tray context menu in Windows 11 colours (light or dark, like the taskbar it opens from) with
/// rounded hover highlights, instead of WinForms' blue Office look.</summary>
internal sealed class TrayMenuRenderer(TrayMenuPalette palette) : ToolStripProfessionalRenderer(new TrayMenuColors(palette))
{
    public TrayMenuPalette Palette { get; } = palette;

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Palette.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Palette.Border);
        var bounds = e.ToolStrip.ClientRectangle;
        e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Palette.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var bounds = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var path = RoundedRectangle(bounds, 4);
        using var brush = new SolidBrush(Palette.Hover);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Palette.Text : Palette.DisabledText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        using var font = new Font("Segoe Fluent Icons", 9f);
        TextRenderer.DrawText(e.Graphics, "", font, e.ImageRectangle, Palette.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Palette.Separator);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class TrayMenuColors(TrayMenuPalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Background;

        public override Color MenuBorder => palette.Border;

        public override Color MenuItemBorder => palette.Hover;

        public override Color MenuItemSelected => palette.Hover;

        public override Color ImageMarginGradientBegin => palette.Background;

        public override Color ImageMarginGradientMiddle => palette.Background;

        public override Color ImageMarginGradientEnd => palette.Background;

        public override Color SeparatorDark => palette.Separator;

        public override Color SeparatorLight => palette.Separator;

        public override Color CheckBackground => palette.Background;

        public override Color CheckSelectedBackground => palette.Hover;

        public override Color CheckPressedBackground => palette.Hover;
    }
}

internal sealed record TrayMenuPalette(Color Background, Color Hover, Color Text, Color DisabledText, Color Border, Color Separator)
{
    public static TrayMenuPalette Light { get; } = new(
        Color.FromArgb(0xF9, 0xF9, 0xF9), Color.FromArgb(0xEA, 0xEA, 0xEA), Color.FromArgb(0x1A, 0x1A, 0x1A),
        Color.FromArgb(0xA0, 0xA0, 0xA0), Color.FromArgb(0xE0, 0xE0, 0xE0), Color.FromArgb(0xE5, 0xE5, 0xE5));

    public static TrayMenuPalette Dark { get; } = new(
        Color.FromArgb(0x2C, 0x2C, 0x2C), Color.FromArgb(0x3A, 0x3A, 0x3A), Color.White,
        Color.FromArgb(0x78, 0x78, 0x78), Color.FromArgb(0x45, 0x45, 0x45), Color.FromArgb(0x40, 0x40, 0x40));
}
