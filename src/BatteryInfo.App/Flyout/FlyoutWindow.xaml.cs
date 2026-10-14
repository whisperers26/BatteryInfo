using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BatteryInfo.App.Interop;
using BatteryInfo.App.ViewModels;

namespace BatteryInfo.App.Flyout;

/// <summary>The borderless, rounded flyout shown above the notification area. It hides (it is never closed)
/// when it loses focus, on Escape, or on a second click of the tray icon.</summary>
public partial class FlyoutWindow : Window
{
    // A tray click first deactivates (hides) the flyout, then arrives as a click: treat it as the same toggle.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(400);
    private long _hiddenAt;
    private bool _closingForExit;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => NativeMethods.SetCornerPreference(Handle, NativeMethods.DwmwcpRound);
        Deactivated += (_, _) => HideFlyout();
        SizeChanged += (_, _) =>
        {
            if (IsVisible) FlyoutPositioner.Position(this);
        };
        DpiChanged += (_, _) =>
        {
            if (IsVisible) FlyoutPositioner.Position(this);
        };
    }

    /// <summary>True right after the flyout hid itself, so the tray click that caused it does not reopen it.</summary>
    public bool JustHidden => Environment.TickCount64 - _hiddenAt < ReopenGuard.TotalMilliseconds;

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    public void ShowNearTray()
    {
        new WindowInteropHelper(this).EnsureHandle();
        FlyoutPositioner.Position(this);
        Show();
        FlyoutPositioner.Position(this);
        Activate();
        NativeMethods.SetForegroundWindow(Handle);
    }

    public void HideFlyout()
    {
        if (!IsVisible) return;
        Hide();
        _hiddenAt = Environment.TickCount64;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideFlyout();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Closes the window for good (on exit).</summary>
    public void CloseForExit()
    {
        _closingForExit = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 hides it like any other dismissal; the app reuses the window.
        if (!_closingForExit)
        {
            e.Cancel = true;
            HideFlyout();
        }
        base.OnClosing(e);
    }
}
