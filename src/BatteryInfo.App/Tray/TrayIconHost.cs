using System.Windows.Forms;
using BatteryInfo.App.Interop;
using BatteryInfo.Core.Presentation;
using Microsoft.Win32;

namespace BatteryInfo.App.Tray;

/// <summary>The notification-area icon: battery glyph (full, or the first device's level), tooltip summary, left click and the context menu. Create,
/// use and dispose it on the UI thread.</summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _startWithWindows;
    private readonly ToolStripMenuItem _update;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private System.Drawing.Icon? _image;
    private bool _lightTaskbar;
    private int _imageSize;
    private string _glyph = TrayIconRenderer.BatteryGlyph;
    private string? _imageGlyph;
    private bool _low;
    private bool _imageLow;

    public TrayIconHost(System.Windows.Threading.Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Padding = new Padding(0, 4, 0, 4) };
        _menu.Items.Add(MenuItem("Refresh", () => RefreshClicked?.Invoke(this, EventArgs.Empty)));
        _startWithWindows = MenuItem("Start with Windows", () => StartWithWindowsClicked?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(_startWithWindows);
        _update = MenuItem(UpdateText.CheckLabel, () => UpdateClicked?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(_update);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(MenuItem("Exit", () => ExitClicked?.Invoke(this, EventArgs.Empty)));
        _menu.Opening += (_, _) =>
        {
            ApplyMenuTheme();
            _startWithWindows.Checked = IsStartWithWindows?.Invoke() ?? false;
        };
        _menu.Opened += (_, _) => NativeMethods.SetCornerPreference(_menu.Handle, NativeMethods.DwmwcpRoundSmall);

        _icon = new NotifyIcon { Text = TrayTooltip.Title, ContextMenuStrip = _menu };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) LeftClicked?.Invoke(this, EventArgs.Empty);
        };
        UpdateIcon();
        _icon.Visible = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public event EventHandler? LeftClicked;

    public event EventHandler? RefreshClicked;

    public event EventHandler? ExitClicked;

    public event EventHandler? StartWithWindowsClicked;

    /// <summary>The update item: "Check for updates", or "Restart to update" once one is downloaded.</summary>
    public event EventHandler? UpdateClicked;

    public void SetUpdateItem(string text, bool enabled)
    {
        _update.Text = text;
        _update.Enabled = enabled;
    }

    /// <summary>Asked each time the menu opens, so the check mark follows the registry.</summary>
    public Func<bool>? IsStartWithWindows { get; set; }

    /// <summary>Draws the battery filled to this level, red when low; null draws it full.</summary>
    public void SetLevel(TrayLevel? level, bool low)
    {
        _glyph = TrayIconRenderer.GlyphFor(level);
        _low = level is not null && low;
        UpdateIcon();
    }

    /// <summary>Sets the tooltip; Windows allows at most 127 characters.</summary>
    public void SetTooltip(string text) => _icon.Text = text.Length <= TrayTooltip.MaxLength ? text : text[..TrayTooltip.MaxLength];

    /// <summary>Shows a warning notification (a toast on Windows 11). The text is cut to the 255 characters Windows allows.</summary>
    public void ShowWarning(string title, string text)
    {
        const int maxText = 255;
        _icon.ShowBalloonTip(5000, title, text.Length <= maxText ? text : text[..(maxText - 1)] + "…", ToolTipIcon.Warning);
    }

    /// <summary>Shows an information notification (a toast on Windows 11).</summary>
    public void ShowInfo(string title, string text) => _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
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

    // A display scale or monitor change (WM_DISPLAYCHANGE) may move the taskbar to another DPI.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(UpdateIcon);

    // Drawn at the exact size the shell shows, so it never resamples the glyph's whole-pixel strokes.
    private void UpdateIcon()
    {
        var light = TaskbarTheme.IsLight();
        var size = TrayIconRenderer.TraySize();
        if (_image is not null && _lightTaskbar == light && _imageSize == size && _imageGlyph == _glyph && _imageLow == _low) return;
        _lightTaskbar = light;
        _imageSize = size;
        _imageGlyph = _glyph;
        _imageLow = _low;
        var previous = _image;
        _image = TrayIconRenderer.Render(_glyph, light, _low, size);
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
