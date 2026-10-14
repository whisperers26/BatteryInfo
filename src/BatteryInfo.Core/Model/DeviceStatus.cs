namespace BatteryInfo.Core.Model;

public enum DeviceStatus
{
    /// <summary>The battery was read just now.</summary>
    Online,

    /// <summary>The receiver or link is present but the device did not answer (sleeping, off, out of range).</summary>
    Asleep,

    /// <summary>Not seen in the latest poll. Set by <c>DeviceStore</c>, never by providers.</summary>
    Offline,

    /// <summary>Present, but Windows denied access to it.</summary>
    NeedsAccess,

    /// <summary>An unexpected failure.</summary>
    Error,
}
