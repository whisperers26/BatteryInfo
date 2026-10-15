using BatteryInfo.Core.Model;

namespace BatteryInfo.App.Demo;

/// <summary>The sample devices of --demo: one per state the flyout can show.</summary>
internal static class DemoDevices
{
    public static readonly DeviceReading Mouse = Template("logitech:D3M01004", "Logitech", "PRO X2 SUPERSTRIKE", DeviceKind.Mouse, "HID++ 0x1004");

    public static readonly DeviceReading Keyboard = Template("rog:azoth-x:DEMO0001", "ROG", "ROG Azoth X", DeviceKind.Keyboard, "ROG 12 01");

    public static readonly DeviceReading FreeClip = Template("huawei:A1B2C3D4E5F6", "HUAWEI", "HUAWEI FreeClip 2", DeviceKind.Earbuds, "SPP");

    public static readonly DeviceReading FreeBuds = Template("huawei:A1B2C3D4E5F7", "HUAWEI", "HUAWEI FreeBuds 6i", DeviceKind.Earbuds, DeviceReading.WindowsHfpSource) with
    {
        Approximate = true,
        Detail = "The battery channel is busy, so this is Windows' own value",
    };

    public static readonly DeviceReading AirPods = Template("apple:A1B2C3D4E5F8", "Apple", "AirPods Pro 2", DeviceKind.Earbuds,
        DeviceReading.AppleBroadcastSource) with
    {
        Approximate = true,
    };

    public static readonly DeviceReading Controller = Template("betop:KP70C2", "Betop", "Betop KP70C2", DeviceKind.Controller, "Betop 0x15");

    public static readonly DeviceReading Razer = Template("razer:00D6", "Razer", "Razer Basilisk V3 Pro 35K", DeviceKind.Mouse, "Razer 07/80") with
    {
        Status = DeviceStatus.NeedsAccess,
        Detail = "Windows denied access: install Razer's driver",
    };

    /// <summary>Only in the history: shows as "Not connected" with its last values.</summary>
    public static readonly DeviceReading Cetra = Template("rog:cetra:DEMO0002", "ROG", "ROG Cetra Open Wireless", DeviceKind.Earbuds, "ROG 12 12");

    private static DeviceReading Template(string id, string vendor, string name, DeviceKind kind, string source) =>
        new(id, vendor, name, kind, DeviceStatus.Online, [], DateTimeOffset.MinValue, source);
}
