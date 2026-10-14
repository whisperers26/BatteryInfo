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
    private IntPtr _foregroundHook;
    private DeviceViewModel? _pressedRow;
    private Point _pressedAt;
    private bool _dragging;
    private NativeMethods.WinEventProc? _foregroundChanged; // kept in a field: Windows calls it while hooked

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => NativeMethods.SetCornerPreference(Handle, NativeMethods.DwmwcpRound);
        Deactivated += (_, _) => HideFlyout();
        IsVisibleChanged += (_, e) => viewModel.IsOpen = (bool)e.NewValue;
        SizeChanged += (_, _) =>
        {
            if (IsVisible) FlyoutPositioner.Position(this);
        };
        DpiChanged += (_, _) =>
        {
            if (IsVisible) FlyoutPositioner.Position(this);
        };
        DeviceList.PreviewMouseLeftButtonDown += OnRowPressed;
        DeviceList.PreviewMouseMove += OnRowDragged;
        DeviceList.PreviewMouseLeftButtonUp += OnRowReleased;
        // LostMouseCapture bubbles: taking the capture from the row's toggle button raises it here too. Only the list's
        // own loss ends the drag.
        DeviceList.LostMouseCapture += (_, e) =>
        {
            if (e.OriginalSource == DeviceList) EndDrag();
        };
    }

    private FlyoutViewModel ViewModel => (FlyoutViewModel)DataContext;

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
        WatchForeground();
    }

    public void HideFlyout()
    {
        StopWatchingForeground();
        if (!IsVisible) return;
        Hide();
        _hiddenAt = Environment.TickCount64;
    }

    // Windows may refuse to activate the flyout (no foreground rights, e.g. when opened by a second launch), and then
    // Deactivated never comes. Any other process's window coming to the front closes it instead.
    private void WatchForeground()
    {
        if (_foregroundHook != IntPtr.Zero) return;
        _foregroundChanged ??= (_, _, _, _, _, _, _) => HideFlyout();
        _foregroundHook = NativeMethods.SetWinEventHook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground,
            IntPtr.Zero, _foregroundChanged, 0, 0, NativeMethods.WinEventOutOfContext | NativeMethods.WinEventSkipOwnProcess);
    }

    private void StopWatchingForeground()
    {
        if (_foregroundHook == IntPtr.Zero) return;
        NativeMethods.UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
    }

    // A press on a row may become a drag once the mouse moves far enough; until then it is an ordinary click.
    private void OnRowPressed(object sender, MouseButtonEventArgs e)
    {
        _pressedRow = e.OriginalSource is DependencyObject source && DeviceList.ContainerFromElement(source) is FrameworkElement row
            ? row.DataContext as DeviceViewModel
            : null;
        _pressedAt = e.GetPosition(DeviceList);
    }

    // Capturing the mouse takes it from the row's toggle button, so the press no longer counts as a click.
    private void OnRowDragged(object sender, MouseEventArgs e)
    {
        if (_pressedRow is not { } row || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(DeviceList);
        if (!_dragging)
        {
            if (Math.Abs(at.Y - _pressedAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging = true;
            DeviceList.CaptureMouse();
            Mouse.OverrideCursor = Cursors.SizeNS;
        }
        SetDraggedLook(row, true);
        ViewModel.MoveDevice(row, DropIndex(row, at.Y));
    }

    private void OnRowReleased(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        e.Handled = true;
        DeviceList.ReleaseMouseCapture();
    }

    private void EndDrag()
    {
        if (_dragging && _pressedRow is { } row) SetDraggedLook(row, false);
        _dragging = false;
        _pressedRow = null;
        Mouse.OverrideCursor = null;
    }

    // The dragged row goes below every other row whose middle is above the pointer.
    private int DropIndex(DeviceViewModel dragged, double y)
    {
        var index = 0;
        foreach (var device in ViewModel.Devices)
        {
            if (device == dragged || DeviceList.ItemContainerGenerator.ContainerFromItem(device) is not FrameworkElement row) continue;
            var top = row.TranslatePoint(new Point(0, 0), DeviceList).Y;
            if (top + row.ActualHeight / 2 < y) index++;
        }
        return index;
    }

    private void SetDraggedLook(DeviceViewModel device, bool dragged)
    {
        if (DeviceList.ItemContainerGenerator.ContainerFromItem(device) is UIElement row) row.Opacity = dragged ? 0.6 : 1;
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
        StopWatchingForeground();
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
