using System.Runtime.InteropServices;

namespace BatteryInfo.App.Interop;

internal static partial class NativeMethods
{
    public const int SmCxSmIcon = 49;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();
}
