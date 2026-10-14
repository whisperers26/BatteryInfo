using System.Collections.Concurrent;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.Core.Tests.App;

public sealed class LatestLoadTests
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RunAsync_returns_to_the_caller_while_the_load_is_still_running()
    {
        var loads = new LatestLoad();
        using var gate = new ManualResetEventSlim();
        var applied = new List<string>();

        var run = loads.RunAsync(() => gate.Wait(GateTimeout) ? "history" : "timed out", applied.Add);

        Assert.False(run.IsCompleted);
        gate.Set();
        await run;
        Assert.Equal(["history"], applied);
    }

    [Fact]
    public async Task A_newer_load_drops_the_result_of_an_older_one()
    {
        var loads = new LatestLoad();
        using var gate = new ManualResetEventSlim();
        var applied = new List<string>();

        var older = loads.RunAsync(() => gate.Wait(GateTimeout) ? "older" : "timed out", applied.Add);
        await loads.RunAsync(() => "newer", applied.Add);
        gate.Set();
        await older;

        Assert.Equal(["newer"], applied);
    }

    [Fact]
    public async Task Cancel_drops_the_result_of_a_pending_load()
    {
        var loads = new LatestLoad();
        using var gate = new ManualResetEventSlim();
        var applied = new List<string>();

        var run = loads.RunAsync(() => gate.Wait(GateTimeout) ? "history" : "timed out", applied.Add);
        loads.Cancel();
        gate.Set();
        await run;

        Assert.Empty(applied);
    }

    [Fact]
    public async Task A_load_that_throws_is_logged_and_applies_nothing()
    {
        var loads = new LatestLoad();
        var applied = new List<string>();
        var lines = new ConcurrentQueue<string>();
        void Collect(string line) => lines.Enqueue(line);
        var marker = Guid.NewGuid().ToString("N");

        Log.LineWritten += Collect;
        try
        {
            await loads.RunAsync<string>(() => throw new IOException(marker), applied.Add);
        }
        finally
        {
            Log.LineWritten -= Collect;
        }

        Assert.Empty(applied);
        Assert.Contains(lines, line => line.Contains("ERROR") && line.Contains(marker));
    }
}
