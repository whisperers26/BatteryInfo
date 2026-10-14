using System.Runtime.InteropServices;

namespace BatteryInfo.App.Interop;

internal static partial class NativeMethods
{
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwcpRound = 2;
    public const int DwmwcpRoundSmall = 3;

    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Makes the process per-monitor DPI aware (v2). Must run before the first window exists; returns
    /// false when the awareness was already set (for example by a manifest).</summary>
    public static bool EnablePerMonitorDpiAwareness() => SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);

    /// <summary>Asks DWM for Windows 11 rounded corners. Harmless on Windows 10, which ignores it.</summary>
    public static void SetCornerPreference(IntPtr hwnd, int preference)
    {
        if (hwnd == IntPtr.Zero) return;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }
}
