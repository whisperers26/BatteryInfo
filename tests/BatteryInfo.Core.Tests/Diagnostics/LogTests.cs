using BatteryInfo.Core.Diagnostics;

namespace BatteryInfo.Core.Tests.Diagnostics;

// Log is static and other tests write to it too, so these run with nothing else in parallel.
[CollectionDefinition(nameof(LogTests), DisableParallelization = true)]
public sealed class LogTestsCollection;

[Collection(nameof(LogTests))]
public sealed class LogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BatteryInfoLogTests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _lines = [];

    public LogTests() => Log.LineWritten += _lines.Add;

    public void Dispose()
    {
        Log.LineWritten -= _lines.Add;
        Log.Configure(null, traceFrames: false);
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Frame_is_silent_when_tracing_is_off()
    {
        Log.Configure(null, traceFrames: false);

        Log.Frame("logitech", outgoing: true, [0x10, 0x01]);

        Assert.Empty(_lines);
    }

    [Fact]
    public void Frame_writes_spaced_uppercase_hex_when_tracing()
    {
        Log.Configure(null, traceFrames: true);

        Log.Frame("logitech", outgoing: true, [0x10, 0x01, 0x06, 0x1c]);
        Log.Frame("logitech", outgoing: false, [0xab]);

        Assert.EndsWith("FRAME logitech >> 10 01 06 1C", _lines[0]);
        Assert.EndsWith("FRAME logitech << AB", _lines[1]);
    }

    [Fact]
    public void Lines_are_written_to_a_dated_file()
    {
        Log.Configure(_dir, traceFrames: false);

        Log.Warn("receiver busy");

        var file = Assert.Single(Directory.GetFiles(_dir, "batteryinfo-*.log"));
        Assert.Contains("WARN receiver busy", File.ReadAllText(file));
    }

    [Fact]
    public void Configure_deletes_logs_older_than_seven_days()
    {
        Directory.CreateDirectory(_dir);
        var old = Path.Combine(_dir, "batteryinfo-20200101.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));

        Log.Configure(_dir, traceFrames: false);

        Assert.False(File.Exists(old));
    }

    [Fact]
    public void Error_includes_exception_message()
    {
        Log.Configure(null, traceFrames: false);

        Log.Error("poll failed", new InvalidOperationException("boom"));

        Assert.Contains("ERROR poll failed: System.InvalidOperationException: boom", _lines.Single());
    }
}
