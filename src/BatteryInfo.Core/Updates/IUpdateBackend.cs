namespace BatteryInfo.Core.Updates;

/// <summary>Where updates come from and how they are installed (Velopack reading this repository's GitHub
/// releases, in the app).</summary>
public interface IUpdateBackend
{
    /// <summary>False for a local or development build, which cannot update itself.</summary>
    bool IsInstalled { get; }

    string? CurrentVersion { get; }

    /// <summary>The newer version available, or null when this one is the latest.</summary>
    Task<string?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the update the last <see cref="CheckAsync"/> found.</summary>
    Task DownloadAsync(CancellationToken cancellationToken);

    /// <summary>Hands the downloaded update to the installer, which waits for this process to exit, installs it and
    /// starts the app again. The app must exit promptly afterwards. (Without this, a downloaded update installs the
    /// next time the app starts.)</summary>
    void ApplyAndRestartAfterExit();
}
