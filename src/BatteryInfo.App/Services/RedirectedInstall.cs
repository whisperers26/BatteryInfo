using System.IO;
using System.Text;
using BatteryInfo.App.Interop;
using BatteryInfo.Core.Diagnostics;

namespace BatteryInfo.App.Services;

/// <summary>Spots a copy started from inside another app's container. A packaged (MSIX) app such as the Claude desktop
/// app runs what it launches in its own job, with %LOCALAPPDATA% and HKCU redirected into its package. A Setup.exe run
/// that way installs into that package, where Windows cannot see it: the Start menu shortcut points at a missing exe,
/// and Start with Windows and the uninstall entry exist only inside the package.</summary>
internal static class RedirectedInstall
{
    public const string Title = "BatteryInfo is installed in a sandbox";
    private const string PackagesFolder = @"\AppData\Local\Packages\";

    /// <summary>The toast text; <paramref name="package"/> is a package folder name such as "Claude_pzs8sxrjxfjjc".</summary>
    public static string Message(string package) =>
        $"This copy runs inside the {package.Split('_')[0]} app's container, so Windows cannot see it: its Start menu shortcut, " +
        "Start with Windows and uninstall do not work. Download BatteryInfoApp-win-Setup.exe and run it from Explorer.";

    /// <summary>The package this process's executable is redirected into, or null when it runs from where it was started.</summary>
    public static string? Current()
    {
        if (Environment.ProcessPath is not { } path) return null;
        try
        {
            using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new StringBuilder(1024);
            var length = NativeMethods.GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, 0);
            return length is > 0 and < 1024 ? PackageOf(path, buffer.ToString()) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not resolve the final path of {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>The package folder name when <paramref name="finalPath"/> (from GetFinalPathNameByHandle) puts the file
    /// in a package's folder although it was started from <paramref name="path"/> outside it; otherwise null.</summary>
    public static string? PackageOf(string path, string finalPath)
    {
        var final = finalPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? finalPath[4..] : finalPath;
        if (string.Equals(final, path, StringComparison.OrdinalIgnoreCase)) return null;
        if (path.Contains(PackagesFolder, StringComparison.OrdinalIgnoreCase)) return null;
        var start = final.IndexOf(PackagesFolder, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var package = final[(start + PackagesFolder.Length)..];
        var end = package.IndexOf('\\');
        return end > 0 ? package[..end] : null;
    }
}
