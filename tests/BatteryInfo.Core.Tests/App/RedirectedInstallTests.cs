using BatteryInfo.App.Services;

namespace BatteryInfo.Core.Tests.App;

public class RedirectedInstallTests
{
    private const string Started = @"C:\Users\me\AppData\Local\BatteryInfoApp\current\BatteryInfo.exe";

    [Fact]
    public void A_copy_whose_file_lives_in_a_package_folder_is_redirected()
    {
        var package = RedirectedInstall.PackageOf(Started,
            @"\\?\C:\Users\me\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\BatteryInfoApp\current\BatteryInfo.exe");

        Assert.Equal("Claude_pzs8sxrjxfjjc", package);
    }

    [Fact]
    public void A_copy_whose_file_is_where_it_was_started_is_not()
    {
        Assert.Null(RedirectedInstall.PackageOf(Started, @"\\?\" +Started.ToUpperInvariant()));
    }

    [Fact]
    public void A_copy_moved_by_some_other_link_is_not_reported()
    {
        Assert.Null(RedirectedInstall.PackageOf(Started, @"\\?\D:\Apps\BatteryInfoApp\current\BatteryInfo.exe"));
    }

    [Fact]
    public void An_app_installed_inside_a_package_folder_on_purpose_is_not_reported()
    {
        const string path = @"C:\Users\me\AppData\Local\Packages\Some_abc\LocalCache\BatteryInfo.exe";

        Assert.Null(RedirectedInstall.PackageOf(path, @"\\?\" +path));
    }
}
