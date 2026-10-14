using BatteryInfo.Core.Diagnostics;

namespace BatteryInfo.Core.Updates;

/// <summary>Checks for, downloads and installs updates. In automatic mode it checks a minute after start and then
/// every six hours; a found update is downloaded and installs the next time the app starts, or at once through
/// <see cref="RestartToUpdate"/>. Events are raised on
/// thread-pool threads.</summary>
public sealed class Updater : IDisposable
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly IUpdateBackend _backend;
    private readonly ITimer _timer;
    private readonly SemaphoreSlim _checking = new(1, 1);
    private readonly object _gate = new();
    private bool _started;
    private bool _automatic = true;
    private string? _announced;
    private UpdateStatus _status;

    public Updater(IUpdateBackend backend, TimeProvider time)
    {
        _backend = backend;
        _status = new UpdateStatus(UpdateState.Idle, backend.CurrentVersion);
        _timer = time.CreateTimer(_ => _ = RunCheckAsync(automatic: true), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<UpdateStatus>? StatusChanged;

    /// <summary>An automatic check downloaded a version not announced before.</summary>
    public event EventHandler<string>? UpdateReady;

    /// <summary>The update is set to install on exit and restart: the app should shut down now.</summary>
    public event EventHandler? ExitRequested;

    public UpdateStatus Status
    {
        get
        {
            lock (_gate) return _status;
        }
    }

    public bool Automatic
    {
        get => _automatic;
        set
        {
            _automatic = value;
            Schedule();
        }
    }

    public void Start()
    {
        _started = true;
        if (!_backend.IsInstalled)
        {
            SetStatus(Unavailable("Updates work only in the app installed from the release's Setup.exe."));
            return;
        }
        Schedule();
    }

    public Task CheckNowAsync(CancellationToken cancellationToken = default) => RunCheckAsync(automatic: false, cancellationToken);

    /// <summary>Installs the downloaded update and restarts the app. Does nothing unless one is ready.</summary>
    public void RestartToUpdate()
    {
        if (Status.State != UpdateState.ReadyToRestart) return;
        Log.Info($"Restarting to install {Status.NewVersion}");
        _backend.ApplyAndRestartAfterExit();
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _checking.Dispose();
    }

    private void Schedule()
    {
        if (!_started || !_backend.IsInstalled) return;
        if (_automatic) _timer.Change(FirstCheckDelay, CheckInterval);
        else _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private async Task RunCheckAsync(bool automatic, CancellationToken cancellationToken = default)
    {
        if (!_backend.IsInstalled)
        {
            SetStatus(Unavailable("Updates work only in the app installed from the release's Setup.exe."));
            return;
        }

        await _checking.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ready = Status.State == UpdateState.ReadyToRestart ? Status : null;
            SetStatus(new UpdateStatus(UpdateState.Checking, _backend.CurrentVersion));
            var version = await _backend.CheckAsync(cancellationToken).ConfigureAwait(false);
            Log.Info($"{(automatic ? "Automatic" : "Manual")} update check: {(version is null ? $"{_backend.CurrentVersion} is up to date" : $"{version} is available")}");
            if (version is null)
            {
                SetStatus(new UpdateStatus(UpdateState.UpToDate, _backend.CurrentVersion));
                return;
            }
            if (ready?.NewVersion == version)
            {
                SetStatus(ready);
                return;
            }

            SetStatus(new UpdateStatus(UpdateState.Downloading, _backend.CurrentVersion, version));
            await _backend.DownloadAsync(cancellationToken).ConfigureAwait(false);
            Log.Info($"Update {version} downloaded; it installs the next time BatteryInfo starts");
            SetStatus(new UpdateStatus(UpdateState.ReadyToRestart, _backend.CurrentVersion, version));

            if (automatic && version != _announced)
            {
                _announced = version;
                UpdateReady?.Invoke(this, version);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.Error("Update check failed", e);
            SetStatus(new UpdateStatus(UpdateState.Failed, _backend.CurrentVersion, Message: e.Message));
        }
        finally
        {
            _checking.Release();
        }
    }

    private UpdateStatus Unavailable(string reason) => new(UpdateState.Unavailable, _backend.CurrentVersion, Message: reason);

    private void SetStatus(UpdateStatus status)
    {
        lock (_gate) _status = status;
        StatusChanged?.Invoke(this, status);
    }
}
