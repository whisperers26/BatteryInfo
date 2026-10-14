using BatteryInfo.App.Interop;
using BatteryInfo.Core.Lifecycle;

namespace BatteryInfo.App;

/// <summary>One running copy per user session (named mutex). A second launch asks the first one to show its
/// flyout through a named event, then exits. Another process (publish.ps1) can ask the running copy to close
/// through its <see cref="ExitRequest"/>.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequest;
    private readonly ExitRequest _exitRequest;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle showRequest, ExitRequest exitRequest)
    {
        _mutex = mutex;
        _showRequest = showRequest;
        _exitRequest = exitRequest;
    }

    /// <summary>Returns null when another instance with this name is already running.</summary>
    public static SingleInstance? TryAcquire(string name)
    {
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(false, $@"Local\{name}.SingleInstance", out createdNew);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // The name is held by something this launch cannot open: an elevated copy started under another logon,
            // or an object of another kind. Either way it is taken, so this launch must not run as well.
            return null;
        }
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.ShowFlyout"),
            ExitRequest.ForCurrentProcess());
    }

    /// <summary>Asks the running instance to show its flyout, passing on this launch's right to come to the front.
    /// Does nothing when the running copy is elevated.</summary>
    public static void RequestShow(string name)
    {
        EventWaitHandle? request;
        try
        {
            if (!EventWaitHandle.TryOpenExisting($@"Local\{name}.ShowFlyout", out request)) return;
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated copy's event lets this launch wait on it but not set it, so its flyout cannot be asked for.
            return;
        }
        NativeMethods.AllowSetForegroundWindow(NativeMethods.AsfwAny);
        using (request) request.Set();
    }

    /// <summary>Runs <paramref name="onShowRequested"/> on a pool thread whenever another launch asks for the flyout.</summary>
    public void ListenForShowRequests(Action onShowRequested) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_showRequest, (_, _) => onShowRequested(), null,
            Timeout.Infinite, executeOnlyOnce: false);

    /// <summary>Runs <paramref name="onExitRequested"/> once, on a pool thread, when another process asks this one to close.</summary>
    public void ListenForExitRequests(Action onExitRequested) => _exitRequest.Listen(onExitRequested);

    public void Dispose()
    {
        _wait?.Unregister(null);
        _exitRequest.Dispose();
        _showRequest.Dispose();
        _mutex.Dispose();
    }
}
