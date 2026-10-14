using System.IO;
using System.Runtime.InteropServices;
using BatteryInfo.App.Interop;
using BatteryInfo.Core.Diagnostics;

namespace BatteryInfo.App.Services;

/// <summary>Points the installer's Start menu and desktop shortcuts at BatteryInfo.ico. Builds before 1.1.1 had no
/// icon, and Explorer keeps the blank icon it cached for "BatteryInfo.exe,0" even after the exe gains one; a new icon
/// path makes it look again. Only shortcuts that start this very exe are touched.</summary>
internal static class ShortcutIcons
{
    public const string IconFileName = "BatteryInfo.ico";
    private const string ShortcutFileName = "BatteryInfo.lnk";

    public static void Repair()
    {
        if (Environment.ProcessPath is not { } exe) return;
        var icon = Path.Combine(Path.GetDirectoryName(exe)!, IconFileName);
        if (!File.Exists(icon)) return;

        var changed = false;
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory })
        {
            var path = Path.Combine(Environment.GetFolderPath(folder), ShortcutFileName);
            if (File.Exists(path)) changed |= Repair(path, exe, icon);
        }
        if (changed) NativeMethods.SHChangeNotify(NativeMethods.ShcneAssocChanged, NativeMethods.ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Sets one shortcut's icon when it starts <paramref name="exe"/>; true when it changed.</summary>
    internal static bool Repair(string path, string exe, string icon)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!);
            dynamic link = shortcut = ((dynamic)shell!).CreateShortcut(path);
            if (!string.Equals((string)link.TargetPath, exe, StringComparison.OrdinalIgnoreCase)) return false;
            var location = icon + ",0";
            if (string.Equals((string)link.IconLocation, location, StringComparison.OrdinalIgnoreCase)) return false;
            link.IconLocation = location;
            link.Save();
            Log.Info($"Set the icon of {path}");
            return true;
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or InvalidCastException
                                      or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Log.Warn($"Could not set the icon of {path}: {e.Message}");
            return false;
        }
        finally
        {
            if (shortcut is not null) Marshal.ReleaseComObject(shortcut);
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
    }
}
