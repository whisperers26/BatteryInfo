using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.Presentation;

/// <summary>Which devices the flyout and tooltip list: only those talking to the PC right now. A device that is
/// asleep, switched off or no longer connected is hidden until it connects again; one that is present but needs
/// attention (access denied, an error) stays visible so the problem can be seen.</summary>
public static class ConnectedDevices
{
    public static bool IsShown(DeviceState state) =>
        state.Latest.Status is DeviceStatus.Online or DeviceStatus.NeedsAccess or DeviceStatus.Error;

    public static IReadOnlyList<DeviceState> Filter(IReadOnlyList<DeviceState> states) => states.Where(IsShown).ToList();
}
