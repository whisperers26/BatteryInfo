using System.Runtime.InteropServices;

namespace BatteryInfo.Core.Interop;

internal static unsafe class CfgMgr32
{
    public const uint CrSuccess = 0;
    public const uint CrBufferSmall = 0x1A;
    public const uint CmGetIdListFilterEnumerator = 0x1;
    public const uint CmGetIdListFilterPresent = 0x100;
    public const uint CmLocateDevnodeNormal = 0;
    public const int MaxDeviceIdLength = 200;

    [StructLayout(LayoutKind.Sequential)]
    public struct DevPropKey(Guid fmtid, uint pid)
    {
        public Guid Fmtid = fmtid;
        public uint Pid = pid;
    }

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_Parent(out uint parentDevInst, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_IDW")]
    public static extern uint CM_Get_Device_ID(uint devInst, char* buffer, uint bufferLength, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")]
    public static extern uint CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_ID_List_SizeW")]
    public static extern uint CM_Get_Device_ID_List_Size(out uint length, string? filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_ID_ListW")]
    public static extern uint CM_Get_Device_ID_List(string? filter, char* buffer, uint bufferLength, uint flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    public static extern uint CM_Get_DevNode_Property(uint devInst, ref DevPropKey propertyKey, out uint propertyType,
        byte* buffer, ref uint bufferSize, uint flags);

    /// <summary>Instance id of a devnode, e.g. USB\VID_046D&amp;PID_C54D&amp;MI_02\B&amp;18153BD2&amp;0&amp;0002.</summary>
    public static string? GetDeviceId(uint devInst)
    {
        var buffer = stackalloc char[MaxDeviceIdLength + 1];
        return CM_Get_Device_ID(devInst, buffer, MaxDeviceIdLength + 1, 0) == CrSuccess ? new string(buffer) : null;
    }

    public static string? GetParentDeviceId(uint devInst) =>
        CM_Get_Parent(out var parent, devInst, 0) == CrSuccess ? GetDeviceId(parent) : null;

    /// <summary>Instance ids of present devices, optionally limited to one enumerator such as "BTHENUM".</summary>
    public static IReadOnlyList<string> GetPresentDeviceIds(string? enumerator)
    {
        var flags = CmGetIdListFilterPresent | (enumerator is null ? 0 : CmGetIdListFilterEnumerator);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_Size(out var length, enumerator, flags) != CrSuccess) return [];
            var buffer = new char[length];
            fixed (char* p = buffer)
            {
                var cr = CM_Get_Device_ID_List(enumerator, p, length, flags);
                if (cr == CrBufferSmall) continue;
                if (cr != CrSuccess) return [];
            }
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        return [];
    }
}
