using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Tests.Monitoring;

public sealed class LowBatteryAlertStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BatteryInfoTests", Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_directory, "low-battery-alerts.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Load_returns_none_when_the_file_is_missing()
    {
        Assert.Empty(new LowBatteryAlertStore(StorePath).Load());
    }

    [Fact]
    public void Save_then_Load_round_trips_and_creates_the_directory()
    {
        new LowBatteryAlertStore(StorePath).Save(["logitech:1|", "huawei:1|Left"]);

        Assert.True(File.Exists(StorePath));
        Assert.Equal(["huawei:1|Left", "logitech:1|"], new LowBatteryAlertStore(StorePath).Load().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Save_overwrites_the_previous_file()
    {
        var store = new LowBatteryAlertStore(StorePath);
        store.Save(["logitech:1|"]);

        store.Save([]);

        Assert.Empty(store.Load());
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("[ \"logitech:1|\", ")]
    [InlineData("null")]
    [InlineData("{ \"logitech:1|\": true }")]
    public void Load_returns_none_when_the_file_is_not_a_list_of_keys(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, json);

        Assert.Empty(new LowBatteryAlertStore(StorePath).Load());
    }

    [Fact]
    public void Load_skips_null_entries()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, """[ "logitech:1|", null ]""");

        Assert.Equal(["logitech:1|"], new LowBatteryAlertStore(StorePath).Load());
    }

    [Fact]
    public void Save_does_not_throw_when_the_path_is_a_directory()
    {
        Directory.CreateDirectory(StorePath);

        new LowBatteryAlertStore(StorePath).Save(["logitech:1|"]);

        Assert.Empty(new LowBatteryAlertStore(StorePath).Load());
    }
}
