using BatteryInfo.Core.Settings;

namespace BatteryInfo.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BatteryInfoTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Defaults_match_the_spec()
    {
        var settings = new AppSettings();

        Assert.Equal(120, settings.PollIntervalSeconds);
        Assert.Equal(20, settings.LowBatteryThreshold);
        Assert.True(settings.NotificationsEnabled);
        Assert.False(settings.TraceFrames);
    }

    [Fact]
    public void Load_returns_defaults_when_the_file_is_missing()
    {
        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(new AppSettings(), settings);
    }

    [Fact]
    public void Load_returns_defaults_when_the_file_is_corrupt()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ \"LowBatteryThreshold\": 25, ");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(new AppSettings(), settings);
    }

    [Fact]
    public void Save_then_Load_round_trips_and_creates_the_directory()
    {
        var store = new SettingsStore(SettingsPath);
        var saved = new AppSettings(PollIntervalSeconds: 300, LowBatteryThreshold: 15, NotificationsEnabled: false, TraceFrames: true);

        store.Save(saved);

        Assert.True(File.Exists(SettingsPath));
        Assert.Equal(saved, new SettingsStore(SettingsPath).Load());
    }

    [Fact]
    public void Load_keeps_defaults_for_properties_the_file_does_not_mention()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{ "LowBatteryThreshold": 25 }""");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(new AppSettings(LowBatteryThreshold: 25), settings);
    }

    [Theory]
    [InlineData("""{ "PollIntervalSeconds": 0 }""")]
    [InlineData("""{ "PollIntervalSeconds": -5 }""")]
    [InlineData("""{ "LowBatteryThreshold": 0 }""")]
    [InlineData("""{ "LowBatteryThreshold": 101 }""")]
    public void Load_replaces_out_of_range_values_with_defaults(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, json);

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(new AppSettings(), settings);
    }

    [Fact]
    public void Load_returns_defaults_when_the_file_holds_json_null()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "null");

        Assert.Equal(new AppSettings(), new SettingsStore(SettingsPath).Load());
    }

    [Fact]
    public void Save_overwrites_the_previous_file()
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(new AppSettings(LowBatteryThreshold: 30));

        store.Save(new AppSettings(LowBatteryThreshold: 10));

        Assert.Equal(10, store.Load().LowBatteryThreshold);
        Assert.Single(Directory.GetFiles(_directory));
    }
}
