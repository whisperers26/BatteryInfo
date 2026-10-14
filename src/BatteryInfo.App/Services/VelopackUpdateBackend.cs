using BatteryInfo.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace BatteryInfo.App.Services;

/// <summary>Updates from this repository's public GitHub releases, which the release workflow fills with Velopack
/// packages. A build not installed through Setup.exe reports itself as not installed.</summary>
internal sealed class VelopackUpdateBackend : IUpdateBackend
{
    public const string RepositoryUrl = "https://github.com/whisperers26/BatteryInfo";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _found;

    public bool IsInstalled => _manager.IsInstalled;

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _found?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(CancellationToken cancellationToken) =>
        _found is null
            ? throw new InvalidOperationException("No update was found to download.")
            : _manager.DownloadUpdatesAsync(_found, progress: null, cancelToken: cancellationToken);

    // Velopack's updater waits (up to 60 s) for this process to exit, installs, and starts the new version in the tray.
    // A downloaded update that is never handed over here is installed by VelopackApp on the next start instead.
    public void ApplyAndRestartAfterExit()
    {
        if (_found is null) return;
        _manager.WaitExitThenApplyUpdates(_found.TargetFullRelease, silent: true, restart: true, restartArgs: ["--tray"]);
    }
}
