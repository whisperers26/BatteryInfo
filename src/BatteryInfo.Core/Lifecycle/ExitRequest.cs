namespace BatteryInfo.Core.Lifecycle;

/// <summary>A named event, keyed by process id, that another process in the same session sets to ask this one to
/// close cleanly. <c>scripts/publish.ps1</c> sets it before it replaces a running copy: a killed process cannot remove
/// its tray icon, which then stays in the notification area until the mouse passes over it.</summary>
public sealed class ExitRequest : IDisposable
{
    private readonly EventWaitHandle _event;
    private RegisteredWaitHandle? _wait;

    private ExitRequest(EventWaitHandle exitEvent) => _event = exitEvent;

    /// <summary>The event's name for a process. publish.ps1 builds the same name from the id of the process it found.</summary>
    public static string EventName(int processId) => $@"Local\BatteryInfo.Exit.{processId}";

    /// <summary>Creates this process's event; it exists until <see cref="Dispose"/>.</summary>
    public static ExitRequest ForCurrentProcess() =>
        new(new EventWaitHandle(false, EventResetMode.AutoReset, EventName(Environment.ProcessId)));

    /// <summary>Runs <paramref name="onExitRequested"/> once, on a pool thread, when the event is set, including when it
    /// was set before this call.</summary>
    public void Listen(Action onExitRequested) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onExitRequested(), null, Timeout.Infinite,
            executeOnlyOnce: true);

    public void Dispose()
    {
        _wait?.Unregister(null);
        _event.Dispose();
    }
}
