namespace BatteryInfo.Core.Presentation;

/// <summary>The Start with Windows switch that the tray menu and the flyout share. A real switch reads and writes
/// the autostart entry through <paramref name="isEnabled"/> and <paramref name="setEnabled"/>. A sandboxed switch
/// (the --demo instance) starts off and keeps its value in memory, so trying it never touches the autostart entry
/// that the installed app starts from. Use from the UI thread.</summary>
public sealed class StartWithWindowsSwitch(bool sandboxed, Func<bool> isEnabled, Action<bool> setEnabled)
{
    private bool _sandboxedValue;

    public bool IsOn => sandboxed ? _sandboxedValue : isEnabled();

    public void Set(bool on)
    {
        if (sandboxed) _sandboxedValue = on;
        else setEnabled(on);
    }

    public void Toggle() => Set(!IsOn);
}
