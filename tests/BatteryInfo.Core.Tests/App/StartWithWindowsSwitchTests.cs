using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public sealed class StartWithWindowsSwitchTests
{
    [Fact]
    public void A_real_switch_reads_the_autostart_entry()
    {
        var entry = new FakeAutoStartEntry { IsEnabled = true };
        var startWithWindows = new StartWithWindowsSwitch(sandboxed: false, entry.Read, entry.Write);

        Assert.True(startWithWindows.IsOn);
        entry.IsEnabled = false;
        Assert.False(startWithWindows.IsOn);
    }

    [Fact]
    public void A_real_switch_writes_the_autostart_entry()
    {
        var entry = new FakeAutoStartEntry();
        var startWithWindows = new StartWithWindowsSwitch(sandboxed: false, entry.Read, entry.Write);

        startWithWindows.Toggle();

        Assert.Equal([true], entry.Writes);
        Assert.True(startWithWindows.IsOn);
    }

    [Fact]
    public void A_sandboxed_switch_starts_off_without_reading_the_autostart_entry()
    {
        var entry = new FakeAutoStartEntry { IsEnabled = true };
        var startWithWindows = new StartWithWindowsSwitch(sandboxed: true, entry.Read, entry.Write);

        Assert.False(startWithWindows.IsOn);
        Assert.Equal(0, entry.Reads);
    }

    [Fact]
    public void A_sandboxed_switch_keeps_its_value_in_memory_and_never_writes_the_autostart_entry()
    {
        var entry = new FakeAutoStartEntry { IsEnabled = true };
        var startWithWindows = new StartWithWindowsSwitch(sandboxed: true, entry.Read, entry.Write);

        startWithWindows.Set(true);
        Assert.True(startWithWindows.IsOn);
        startWithWindows.Toggle();
        Assert.False(startWithWindows.IsOn);

        Assert.Empty(entry.Writes);
        Assert.Equal(0, entry.Reads);
        Assert.True(entry.IsEnabled);
    }

    /// <summary>Stands in for HKCU\...\Run\BatteryInfo and records every access.</summary>
    private sealed class FakeAutoStartEntry
    {
        public bool IsEnabled { get; set; }

        public int Reads { get; private set; }

        public List<bool> Writes { get; } = [];

        public bool Read()
        {
            Reads++;
            return IsEnabled;
        }

        public void Write(bool enabled)
        {
            Writes.Add(enabled);
            IsEnabled = enabled;
        }
    }
}
