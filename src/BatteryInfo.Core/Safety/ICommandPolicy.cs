using BatteryInfo.Core.Hid;

namespace BatteryInfo.Core.Safety;

/// <summary>A vendor's allow-list: the only frames that may ever be sent to its devices.</summary>
public interface ICommandPolicy
{
    bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing);
}
