using BatteryInfo.Core.Diagnostics;

namespace BatteryInfo.Core.Presentation;

/// <summary>Runs loads on the thread pool, so the UI thread never waits on disk, and applies only the newest one:
/// starting another load or calling <see cref="Cancel"/> drops the results of the loads before it. Start and cancel
/// from the UI thread; results are applied back on it (the caller's synchronization context).</summary>
public sealed class LatestLoad
{
    private int _generation;

    /// <summary>Runs <paramref name="load"/> on the thread pool, then <paramref name="apply"/> on the calling context
    /// unless a newer load or a cancel came in between. A load that throws is logged and applies nothing.</summary>
    public async Task RunAsync<T>(Func<T> load, Action<T> apply)
    {
        var generation = Interlocked.Increment(ref _generation);
        T result;
        try
        {
            result = await Task.Run(load);
        }
        catch (Exception e)
        {
            Log.Error("Loading in the background failed", e);
            return;
        }
        if (generation == Volatile.Read(ref _generation)) apply(result);
    }

    /// <summary>Drops the result of any load still running.</summary>
    public void Cancel() => Interlocked.Increment(ref _generation);
}
