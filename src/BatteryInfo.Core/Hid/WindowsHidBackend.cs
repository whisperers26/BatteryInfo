using System.Runtime.InteropServices;
using BatteryInfo.Core.Interop;
using Microsoft.Win32.SafeHandles;

namespace BatteryInfo.Core.Hid;

/// <summary>HID access through SetupAPI and hid.dll.</summary>
public sealed unsafe class WindowsHidBackend : IHidBackend
{
    private static readonly IntPtr InvalidHandle = new(-1);

    public IReadOnlyList<HidDeviceInfo> Enumerate(Func<ushort, ushort, bool>? filter = null)
    {
        HidApi.HidD_GetHidGuid(out var hidGuid);
        var set = SetupApi.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            SetupApi.DigcfPresent | SetupApi.DigcfDeviceInterface);
        if (set == InvalidHandle) return [];

        var devices = new List<HidDeviceInfo>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SetupApi.SpDeviceInterfaceData { CbSize = (uint)Marshal.SizeOf<SetupApi.SpDeviceInterfaceData>() };
                if (!SetupApi.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref interfaceData)) break;

                var devInfo = new SetupApi.SpDevinfoData { CbSize = (uint)Marshal.SizeOf<SetupApi.SpDevinfoData>() };
                if (GetInterfacePath(set, ref interfaceData, ref devInfo) is not { } path) continue;

                var ids = HidDeviceInfo.ParseVendorProduct(path);
                if (filter is not null && (ids is null || !filter(ids.Value.VendorId, ids.Value.ProductId))) continue;

                devices.Add(Describe(path, devInfo.DevInst, ids));
            }
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(set);
        }
        return devices;
    }

    public IHidConnection Open(HidDeviceInfo device, HidAccess access) => WindowsHidConnection.Open(device, access);

    private static string? GetInterfacePath(IntPtr set, ref SetupApi.SpDeviceInterfaceData interfaceData, ref SetupApi.SpDevinfoData devInfo)
    {
        SetupApi.SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, IntPtr.Zero, 0, out var required, ref devInfo);
        if (required == 0) return null;

        var detail = Marshal.AllocHGlobal((int)required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize is the size of the fixed part: 8 on x64, 6 on x86.
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            return SetupApi.SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, detail, required, out _, ref devInfo)
                ? Marshal.PtrToStringUni(detail + 4)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    private static HidDeviceInfo Describe(string path, uint devInst, (ushort VendorId, ushort ProductId)? ids)
    {
        var instanceId = CfgMgr32.GetDeviceId(devInst);
        var parentId = CfgMgr32.GetParentDeviceId(devInst);
        var interfaceNumber = HidDeviceInfo.ParseInterfaceNumber(path);
        var unopened = new HidDeviceInfo(path, ids?.VendorId ?? 0, ids?.ProductId ?? 0, 0, 0, 0, 0, 0, 0,
            interfaceNumber, instanceId, parentId, null, null, null, null, Accessible: false);

        using var handle = Kernel32.CreateFile(path, 0, Kernel32.FileShareRead | Kernel32.FileShareWrite, IntPtr.Zero,
            Kernel32.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return unopened;

        var attributes = new HidApi.HiddAttributes { Size = (uint)Marshal.SizeOf<HidApi.HiddAttributes>() };
        if (!HidApi.HidD_GetAttributes(handle, ref attributes)) return unopened;
        if (!HidApi.HidD_GetPreparsedData(handle, out var preparsed)) return unopened;
        try
        {
            if (HidApi.HidP_GetCaps(preparsed, out var caps) != HidApi.HidpStatusSuccess) return unopened;
            return new HidDeviceInfo(path, attributes.VendorId, attributes.ProductId, attributes.VersionNumber,
                caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength,
                interfaceNumber, instanceId, parentId,
                ReadString(handle, &HidApi.HidD_GetProductString), ReadString(handle, &HidApi.HidD_GetSerialNumberString),
                FirstReportId(HidApi.HidpOutput, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps, preparsed),
                FirstReportId(HidApi.HidpFeature, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps, preparsed),
                Accessible: true);
        }
        finally
        {
            HidApi.HidD_FreePreparsedData(preparsed);
        }
    }

    private static byte? FirstReportId(int reportType, ushort valueCaps, ushort buttonCaps, IntPtr preparsed)
    {
        if (valueCaps > 0)
        {
            var buffer = stackalloc byte[valueCaps * HidApi.CapsEntrySize];
            var count = valueCaps;
            if (HidApi.HidP_GetValueCaps(reportType, buffer, ref count, preparsed) == HidApi.HidpStatusSuccess && count > 0)
                return buffer[HidApi.CapsReportIdOffset];
        }
        if (buttonCaps > 0)
        {
            var buffer = stackalloc byte[buttonCaps * HidApi.CapsEntrySize];
            var count = buttonCaps;
            if (HidApi.HidP_GetButtonCaps(reportType, buffer, ref count, preparsed) == HidApi.HidpStatusSuccess && count > 0)
                return buffer[HidApi.CapsReportIdOffset];
        }
        return null;
    }

    private static string? ReadString(SafeFileHandle handle, delegate*<SafeFileHandle, byte*, uint, bool> read)
    {
        const int size = 254; // HID strings are at most 126 UTF-16 characters plus a terminator.
        var buffer = stackalloc byte[size];
        new Span<byte>(buffer, size).Clear();
        if (!read(handle, buffer, size)) return null;
        var text = new string((char*)buffer).Trim();
        return text.Length == 0 ? null : text;
    }
}
