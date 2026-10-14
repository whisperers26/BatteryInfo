namespace BatteryInfo.Core.Updates;

public enum UpdateState
{
    Idle,
    Checking,
    Downloading,
    UpToDate,

    /// <summary>A newer version is downloaded; it installs on restart or the next time the app starts.</summary>
    ReadyToRestart,

    /// <summary>Updates cannot work here: not a build installed from Setup.exe.</summary>
    Unavailable,
    Failed,
}

/// <param name="NewVersion">The version being downloaded or ready to install.</param>
/// <param name="Message">Why updates are unavailable or what failed.</param>
public sealed record UpdateStatus(UpdateState State, string? CurrentVersion, string? NewVersion = null, string? Message = null);
