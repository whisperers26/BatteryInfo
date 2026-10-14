using BatteryInfo.App.Interop;

namespace BatteryInfo.App;

/// <summary>One running copy per user session (named mutex). A second launch asks the first one to show its
/// flyout through a named event, then exits.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequest;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle showRequest)
    {
        _mutex = mutex;
        _showRequest = showRequest;
    }

    /// <summary>Returns null when another instance with this name is already running.</summary>
    public static SingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(false, $@"Local\{name}.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.ShowFlyout"));
    }

    /// <summary>Asks the running instance to show its flyout, passing on this launch's right to come to the front.</summary>
    public static void RequestShow(string name)
    {
        if (!EventWaitHandle.TryOpenExisting($@"Local\{name}.ShowFlyout", out var request)) return;
        NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
        using (request) request.Set();
    }

    /// <summary>Runs <paramref name="onShowRequested"/> on a pool thread whenever another launch asks for the flyout.</summary>
    public void ListenForShowRequests(Action onShowRequested) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_showRequest, (_, _) => onShowRequested(), null,
            Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _wait?.Unregister(null);
        _showRequest.Dispose();
        _mutex.Dispose();
    }
}
