using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Monitoring;

/// <summary>Reads the batteries of one vendor's devices.</summary>
public interface IBatteryProvider : IDisposable
{
    string Vendor { get; }

    /// <summary>Reads every device of this vendor that is visible right now. Device-level failures become
    /// readings with Status Asleep/NeedsAccess/Error; this throws only for cancellation or bugs.</summary>
    Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken);

    /// <summary>Raised when the provider learns something changed (e.g. a Bluetooth connect) and wants an
    /// early poll. Optional.</summary>
    event EventHandler? RefreshRequested;
}
