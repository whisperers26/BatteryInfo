using System.IO;
using System.Security;
using BatteryInfo.Core.Diagnostics;
using Microsoft.Win32;

namespace BatteryInfo.App.Services;

/// <summary>Start with Windows through HKCU\...\Run\BatteryInfo = "&lt;exe&gt;" --tray. The registry value is the source
/// of truth: the app is "enabled" only when the value points at this very executable.</summary>
internal static class AutoStart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "BatteryInfo";

    private static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");

    public static string CommandFor(string executablePath) => $"\"{executablePath}\" --tray";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string command &&
                   string.Equals(ExecutableOf(command), ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Could not read the autostart value: {e.Message}");
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, CommandFor(ExecutablePath), RegistryValueKind.String);
                Log.Info($"Start with Windows on: {CommandFor(ExecutablePath)}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("Start with Windows off");
            }
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Could not change the autostart value: {e.Message}");
        }
    }

    /// <summary>The executable of a Run command line: the quoted first part, or everything before the arguments.</summary>
    private static string ExecutableOf(string command)
    {
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 0 ? text[1..end] : text[1..];
        }
        var arguments = text.IndexOf(" --", StringComparison.Ordinal);
        return arguments > 0 ? text[..arguments] : text;
    }
}
