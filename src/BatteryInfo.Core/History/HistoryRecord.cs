using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.History;

/// <summary>One line of the battery history: one component of one device at one moment.</summary>
/// <param name="Time">When the value was read (the reading's timestamp).</param>
/// <param name="Component">"" for a single-battery device, otherwise "Left", "Right" or "Case".</param>
public sealed record HistoryRecord(
    DateTimeOffset Time,
    string DeviceId,
    string DeviceName,
    string Vendor,
    DeviceKind Kind,
    string Component,
    int? Percent,
    ChargeState Charge,
    DeviceStatus Status);
