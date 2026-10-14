namespace BatteryInfo.Core.Monitoring;

/// <summary>Signals that devices arrived or went away, so the monitor should look again soon.</summary>
public interface IDeviceChangeSource : IDisposable
{
    event EventHandler? Changed;
}
