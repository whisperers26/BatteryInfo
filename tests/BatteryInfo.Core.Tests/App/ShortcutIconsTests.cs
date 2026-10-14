using BatteryInfo.App.Services;

namespace BatteryInfo.Core.Tests.App;

public sealed class ShortcutIconsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("BatteryInfo.Tests.").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // The shortcut an icon-less build's installer made: "BatteryInfo.exe,0".
    private string Shortcut(string target)
    {
        var path = Path.Combine(_folder, "BatteryInfo.lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        var link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.IconLocation = target + ",0";
        link.Save();
        return path;
    }

    private static string IconLocation(string path)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        return shell.CreateShortcut(path).IconLocation;
    }

    [Fact]
    public void A_shortcut_to_this_exe_gets_the_icon_file()
    {
        var exe = Path.Combine(_folder, "BatteryInfo.exe");
        var icon = Path.Combine(_folder, "BatteryInfo.ico");
        File.WriteAllBytes(exe, []);
        var path = Shortcut(exe);

        Assert.True(ShortcutIcons.Repair(path, exe, icon));
        Assert.Equal(icon + ",0", IconLocation(path), ignoreCase: true);
        Assert.False(ShortcutIcons.Repair(path, exe, icon));
    }

    [Fact]
    public void A_shortcut_to_another_exe_is_left_alone()
    {
        var other = Path.Combine(_folder, "Other.exe");
        File.WriteAllBytes(other, []);
        var path = Shortcut(other);

        Assert.False(ShortcutIcons.Repair(path, Path.Combine(_folder, "BatteryInfo.exe"), Path.Combine(_folder, "BatteryInfo.ico")));
        Assert.Equal(other + ",0", IconLocation(path), ignoreCase: true);
    }
}
