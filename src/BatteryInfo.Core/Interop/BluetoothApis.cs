using System.Runtime.InteropServices;

namespace BatteryInfo.Core.Interop;

/// <summary>Win32 Bluetooth API (bthprops.cpl). Pointers are passed directly, so nothing is marshalled.</summary>
internal static unsafe class BluetoothApis
{
    private const string Library = "bthprops.cpl";
    private const uint ErrorSuccess = 0;

    public readonly record struct Paired(ulong Address, string Name, uint ClassOfDevice, bool Connected);

    // BLUETOOTH_DEVICE_INFO: 560 bytes on x64; Address at 8, fConnected at 20, szName at 64. The BOOLs are flag
    // values (fConnected = 32), so test them for non-zero.
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfo
    {
        public uint Size;
        public ulong Address;
        public uint ClassOfDevice;
        public int Connected;
        public int Remembered;
        public int Authenticated;
        public SystemTime LastSeen;
        public SystemTime LastUsed;
        public fixed char Name[248];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    // BLUETOOTH_DEVICE_SEARCH_PARAMS: 40 bytes on x64.
    [StructLayout(LayoutKind.Sequential)]
    public struct SearchParams
    {
        public uint Size;
        public int ReturnAuthenticated;
        public int ReturnRemembered;
        public int ReturnUnknown;
        public int ReturnConnected;
        public int IssueInquiry;
        public byte TimeoutMultiplier;
        public IntPtr Radio;
    }

    /// <summary>Remembered (paired) devices from Windows' records. No inquiry is issued, so nothing is paged.</summary>
    public static List<Paired> EnumeratePaired()
    {
        var result = new List<Paired>();
        var search = new SearchParams
        {
            Size = (uint)sizeof(SearchParams),
            ReturnAuthenticated = 1,
            ReturnRemembered = 1,
            ReturnUnknown = 0,
            ReturnConnected = 1,
            IssueInquiry = 0,
            TimeoutMultiplier = 0,
            Radio = IntPtr.Zero,
        };
        var info = new DeviceInfo { Size = (uint)sizeof(DeviceInfo) };
        var find = BluetoothFindFirstDevice(&search, &info);
        if (find == IntPtr.Zero) return result; // none paired, or no radio
        try
        {
            do
            {
                result.Add(ToPaired(&info));
                info = new DeviceInfo { Size = (uint)sizeof(DeviceInfo) };
            }
            while (BluetoothFindNextDevice(find, &info) != 0);
        }
        finally
        {
            BluetoothFindDeviceClose(find);
        }
        return result;
    }

    public static Paired? GetDeviceInfo(ulong address)
    {
        var info = new DeviceInfo { Size = (uint)sizeof(DeviceInfo), Address = address };
        return BluetoothGetDeviceInfo(IntPtr.Zero, &info) == ErrorSuccess ? ToPaired(&info) : null;
    }

    private static Paired ToPaired(DeviceInfo* info) =>
        new(info->Address, new string(info->Name).Trim(), info->ClassOfDevice, info->Connected != 0);

    [DllImport(Library, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint BluetoothGetDeviceInfo(IntPtr radio, DeviceInfo* info);

    [DllImport(Library, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr BluetoothFindFirstDevice(SearchParams* searchParams, DeviceInfo* info);

    [DllImport(Library, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int BluetoothFindNextDevice(IntPtr find, DeviceInfo* info);

    [DllImport(Library, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int BluetoothFindDeviceClose(IntPtr find);
}
