using Velopack;

namespace BatteryInfo.App;

/// <summary>Entry point. Velopack runs first: during install, update and uninstall it starts the exe with its own
/// arguments, handles them and exits before any window or tray icon exists.</summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
