using System.IO;
using Microsoft.Win32;

namespace BatteryInfo.App.Tray;

/// <summary>Reads whether the taskbar (the "Windows mode") is light. Apps follow "app mode" through WPF's
/// ThemeMode; the tray icon and its menu sit on the taskbar, so they follow the Windows mode instead.</summary>
internal static class TaskbarTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false; // Windows 11's default taskbar is dark.
        }
    }
}
