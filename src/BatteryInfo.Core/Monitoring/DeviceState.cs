using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Monitoring;

/// <summary>The merged view of one device: its latest reading plus the latest one that had a battery value.</summary>
public sealed record DeviceState(DeviceReading Latest, DeviceReading? LastWithBattery)
{
    /// <summary>The latest components when they carry a value, otherwise the last known (stale) ones.</summary>
    public IReadOnlyList<BatteryComponent> DisplayComponents =>
        Latest.HasBattery ? Latest.Components : LastWithBattery?.Components ?? [];

    public bool IsStale => !Latest.HasBattery && LastWithBattery is not null;

    public DateTimeOffset? BatteryTimestamp => Latest.HasBattery ? Latest.Timestamp : LastWithBattery?.Timestamp;
}
