using System.Windows.Forms;
using BatteryInfo.App.Interop;
using BatteryInfo.Core.Presentation;
using Microsoft.Win32;

namespace BatteryInfo.App.Tray;

/// <summary>The notification-area icon: battery glyph, tooltip summary, left click and the context menu. Create,
/// use and dispose it on the UI thread.</summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private System.Drawing.Icon? _image;
    private bool? _lightTaskbar;

    public TrayIconHost(System.Windows.Threading.Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false, Padding = new Padding(0, 4, 0, 4) };
        _menu.Items.Add(MenuItem("Refresh", () => RefreshClicked?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(MenuItem("Exit", () => ExitClicked?.Invoke(this, EventArgs.Empty)));
        _menu.Opening += (_, _) => ApplyMenuTheme();
        _menu.Opened += (_, _) => NativeMethods.SetCornerPreference(_menu.Handle, NativeMethods.DwmwcpRoundSmall);

        _icon = new NotifyIcon { Text = TrayTooltip.Title, ContextMenuStrip = _menu };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) LeftClicked?.Invoke(this, EventArgs.Empty);
        };
        UpdateIcon();
        _icon.Visible = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public event EventHandler? LeftClicked;

    public event EventHandler? RefreshClicked;

    public event EventHandler? ExitClicked;

    /// <summary>Sets the tooltip; Windows allows at most 127 characters.</summary>
    public void SetTooltip(string text) => _icon.Text = text.Length <= TrayTooltip.MaxLength ? text : text[..TrayTooltip.MaxLength];

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image?.Dispose();
    }

    private static ToolStripMenuItem MenuItem(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { Padding = new Padding(8, 4, 8, 4) };
        item.Click += (_, _) => onClick();
        return item;
    }

    // The taskbar theme or the DPI may have changed: redraw the glyph if needed.
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        _dispatcher.BeginInvoke(UpdateIcon);

    private void UpdateIcon()
    {
        var light = TaskbarTheme.IsLight();
        if (_lightTaskbar == light && _image is not null) return;
        _lightTaskbar = light;
        var previous = _image;
        _image = TrayIconRenderer.Render(TrayIconRenderer.BatteryGlyph, light, TrayIconRenderer.TraySize());
        _icon.Icon = _image;
        previous?.Dispose();
    }

    private void ApplyMenuTheme()
    {
        var palette = TaskbarTheme.IsLight() ? TrayMenuPalette.Light : TrayMenuPalette.Dark;
        if (_menu.Renderer is TrayMenuRenderer current && current.Palette == palette) return;
        _menu.Renderer = new TrayMenuRenderer(palette);
        _menu.BackColor = palette.Background;
        _menu.ForeColor = palette.Text;
    }
}
