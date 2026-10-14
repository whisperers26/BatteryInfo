using System.Globalization;
using BatteryInfo.Core.Interop;

namespace BatteryInfo.Core.Bluetooth;

/// <summary>Windows' own battery value for a Bluetooth device: DEVPKEY {104EA319-6EE2-4701-BD47-8DDBF425BBE5} pid 2
/// (DEVPROP_TYPE_BYTE, 0-100). For classic headsets it comes from HFP and is coarse; it lives on a service child
/// node (e.g. the 0x111E hands-free node), not on the device node.</summary>
public static unsafe class BluetoothBatteryProperty
{
    private const uint DevpropTypeByte = 0x03;
    private static readonly Guid BatteryFmtid = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
    private static readonly string[] Enumerators = ["BTHENUM", "BTHLE", "BTHLEDEVICE"];

    public static string FormatAddress(ulong address) => address.ToString("X12", CultureInfo.InvariantCulture);

    /// <summary>The first battery value found on any present devnode of this Bluetooth address, or null.</summary>
    public static int? Read(ulong bluetoothAddress)
    {
        var address = FormatAddress(bluetoothAddress);
        foreach (var enumerator in Enumerators)
        {
            foreach (var id in CfgMgr32.GetPresentDeviceIds(enumerator))
            {
                if (!id.Contains(address, StringComparison.OrdinalIgnoreCase)) continue;
                if (ReadByte(id) is { } value && value <= 100) return value;
            }
        }
        return null;
    }

    private static byte? ReadByte(string instanceId)
    {
        if (CfgMgr32.CM_Locate_DevNode(out var devInst, instanceId, CfgMgr32.CmLocateDevnodeNormal) != CfgMgr32.CrSuccess) return null;
        var key = new CfgMgr32.DevPropKey(BatteryFmtid, 2);
        byte value;
        uint size = 1;
        var cr = CfgMgr32.CM_Get_DevNode_Property(devInst, ref key, out var type, &value, ref size, 0);
        return cr == CfgMgr32.CrSuccess && type == DevpropTypeByte && size == 1 ? value : null;
    }
}
