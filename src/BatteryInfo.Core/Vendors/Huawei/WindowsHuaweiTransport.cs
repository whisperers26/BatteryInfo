using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using BatteryInfo.Core.Bluetooth;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Interop;
using Windows.Devices.Bluetooth;

namespace BatteryInfo.Core.Vendors.Huawei;

/// <summary>The real Bluetooth side of the HUAWEI provider: paired-device discovery and the connected check from the
/// Win32 Bluetooth API (cached records only, no inquiry or paging), a Winsock RFCOMM socket to the SerialPort service,
/// Windows' HFP battery property, and WinRT connection-status events.</summary>
public sealed unsafe class WindowsHuaweiTransport : IHuaweiTransport
{
    private const AddressFamily AfBth = (AddressFamily)32;
    private const ProtocolType BthProtoRfcomm = (ProtocolType)3;
    private const int AudioVideoMajorClass = 0x04;
    private const string FallbackName = "HUAWEI earbuds";

    // Port 0 with the service UUID makes Windows resolve the RFCOMM channel through SDP (docs/protocols/huawei.md §4).
    private static readonly Guid SerialPortService = new("00001101-0000-1000-8000-00805F9B34FB");
    private static readonly string[] NamePrefixes = ["HUAWEI FreeBuds", "HUAWEI FreeClip", "HUAWEI FreeLace"];
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WatchTimeout = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly Dictionary<ulong, BluetoothDevice?> _watched = [];
    private bool _disposed;

    public event EventHandler? ConnectionChanged;

    internal static int DeviceInfoSize => sizeof(BluetoothApis.DeviceInfo);

    internal static int SearchParamsSize => sizeof(BluetoothApis.SearchParams);

    public IReadOnlyList<HuaweiDevice> FindPaired()
    {
        var paired = BluetoothApis.EnumeratePaired();
        if (paired.Count == 0) return [];

        var instanceIds = CfgMgr32.GetPresentDeviceIds("BTHENUM");
        var found = new List<HuaweiDevice>();
        foreach (var device in paired)
        {
            if (!IsHuaweiEarbuds(device.Name, device.ClassOfDevice, HasHuaweiVendorId(instanceIds, device.Address))) continue;
            found.Add(new HuaweiDevice(device.Address, string.IsNullOrWhiteSpace(device.Name) ? FallbackName : device.Name));
            Watch(device.Address);
        }
        return found;
    }

    public bool IsConnected(ulong address) => BluetoothApis.GetDeviceInfo(address) is { Connected: true };

    public Stream OpenSpp(ulong address, TimeSpan connectTimeout)
    {
        var socket = new Socket(AfBth, SocketType.Stream, BthProtoRfcomm);
        try
        {
            Connect(socket, new BluetoothEndPoint(address, SerialPortService, 0), connectTimeout);
            socket.SendTimeout = (int)SendTimeout.TotalMilliseconds;
            return new GuardedSppStream(new RfcommStream(socket));
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public int? ReadWindowsBattery(ulong address) => BluetoothBatteryProperty.Read(address);

    public void Dispose()
    {
        List<BluetoothDevice> devices;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            devices = _watched.Values.OfType<BluetoothDevice>().ToList();
            _watched.Clear();
        }
        foreach (var device in devices)
        {
            device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            device.Dispose();
        }
    }

    /// <summary>The generic rule of §1: a FreeBuds/FreeClip/FreeLace name, or the Bluetooth SIG vendor id of HUAWEI
    /// (0x027D) on a device whose class is audio/video, so HUAWEI phones, laptops and watches never match.</summary>
    internal static bool IsHuaweiEarbuds(string name, uint classOfDevice, bool hasHuaweiVendorId)
    {
        if (NamePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return true;
        return hasHuaweiVendorId && ((classOfDevice >> 8) & 0x1F) == AudioVideoMajorClass;
    }

    /// <summary>Whether a BTHENUM node of this address carries "_VID&amp;0001027D" (vendor id source 1 = Bluetooth SIG,
    /// company 0x027D = HUAWEI), as in BTHENUM\{...}_VID&amp;0001027D_PID&amp;4113\B&amp;23FB693A&amp;0&amp;C0DA5EA83EC7_C00000000.</summary>
    internal static bool HasHuaweiVendorId(IEnumerable<string> instanceIds, ulong address)
    {
        var hex = BluetoothBatteryProperty.FormatAddress(address);
        return instanceIds.Any(id => id.Contains(hex, StringComparison.OrdinalIgnoreCase) &&
                                     id.Contains("_VID&0001027D", StringComparison.OrdinalIgnoreCase));
    }

    // Socket.Connect has no timeout; closing the socket from a timer aborts a connect that takes too long.
    private static void Connect(Socket socket, EndPoint endPoint, TimeSpan timeout)
    {
        const int connecting = 0, connected = 1, timedOut = 2;
        var state = connecting;
        using var timer = new Timer(_ =>
        {
            if (Interlocked.CompareExchange(ref state, timedOut, connecting) == connecting) socket.Dispose();
        }, null, timeout, Timeout.InfiniteTimeSpan);

        try
        {
            socket.Connect(endPoint);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException && Volatile.Read(ref state) == timedOut)
        {
            throw new TimeoutException($"RFCOMM connect took longer than {timeout.TotalSeconds:0.#} s", e);
        }

        if (Interlocked.CompareExchange(ref state, connected, connecting) != connecting)
        {
            throw new TimeoutException($"RFCOMM connect took longer than {timeout.TotalSeconds:0.#} s");
        }
    }

    // Subscribes once per device to WinRT connection-status changes. Creating the BluetoothDevice reads Windows'
    // record of it; it does not connect to it.
    private void Watch(ulong address)
    {
        lock (_gate)
        {
            if (_disposed || !_watched.TryAdd(address, null)) return;
        }

        BluetoothDevice? device = null;
        try
        {
            var lookup = BluetoothDevice.FromBluetoothAddressAsync(address).AsTask();
            if (lookup.Wait(WatchTimeout)) device = lookup.Result;
        }
        catch (Exception e)
        {
            Log.Warn($"HUAWEI: cannot watch Bluetooth connection changes of {BluetoothBatteryProperty.FormatAddress(address)} ({e.Message})");
        }

        if (device is null)
        {
            lock (_gate) _watched.Remove(address);
            return;
        }

        device.ConnectionStatusChanged += OnConnectionStatusChanged;
        lock (_gate)
        {
            if (!_disposed)
            {
                _watched[address] = device;
                return;
            }
        }
        device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        device.Dispose();
    }

    private void OnConnectionStatusChanged(BluetoothDevice sender, object args)
    {
        try
        {
            Log.Info($"{sender.Name}: Bluetooth {sender.ConnectionStatus}");
        }
        catch (Exception)
        {
            // Logging must never stop the refresh below.
        }
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An RFCOMM socket as a stream. Reads wait at most half a second for data (select), then throw
    /// <see cref="TimeoutException"/>, so a quiet link never leaves the socket in the undefined state a receive
    /// timeout would. Socket errors surface as <see cref="IOException"/>.</summary>
    private sealed class RfcommStream(Socket socket) : Stream
    {
        private static readonly TimeSpan ReadWait = TimeSpan.FromMilliseconds(500);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            try
            {
                if (!socket.Poll(ReadWait, SelectMode.SelectRead)) throw new TimeoutException("No SPP data within the read wait");
                return socket.Receive(buffer);
            }
            catch (SocketException e)
            {
                throw new IOException($"SPP receive failed (error {e.ErrorCode})", e);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            try
            {
                while (!buffer.IsEmpty) buffer = buffer[socket.Send(buffer)..];
            }
            catch (SocketException e)
            {
                throw new IOException($"SPP send failed (error {e.ErrorCode})", e);
            }
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException)
                {
                }
                socket.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Win32 Bluetooth API (bthprops.cpl). Pointers are passed directly, so nothing is marshalled.</summary>
    private static class BluetoothApis
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
}

/// <summary>SOCKADDR_BTH as an <see cref="EndPoint"/> (30 bytes, packed): family 32 (u16), Bluetooth address (u64),
/// service class id (GUID), port (u32), all little-endian.</summary>
internal sealed class BluetoothEndPoint(ulong address, Guid serviceClassId, uint port) : EndPoint
{
    private const int SocketAddressSize = 30;

    public ulong Address { get; } = address;

    public Guid ServiceClassId { get; } = serviceClassId;

    public uint Port { get; } = port;

    public override AddressFamily AddressFamily => (AddressFamily)32;

    public override SocketAddress Serialize()
    {
        var address = new SocketAddress(AddressFamily, SocketAddressSize);
        Span<byte> bytes = stackalloc byte[SocketAddressSize - 2];
        BitConverter.TryWriteBytes(bytes[..8], Address);
        ServiceClassId.TryWriteBytes(bytes[8..24]);
        BitConverter.TryWriteBytes(bytes[24..], Port);
        for (var i = 0; i < bytes.Length; i++) address[i + 2] = bytes[i];
        return address;
    }

    public override EndPoint Create(SocketAddress socketAddress)
    {
        Span<byte> bytes = stackalloc byte[SocketAddressSize];
        for (var i = 0; i < SocketAddressSize && i < socketAddress.Size; i++) bytes[i] = socketAddress[i];
        return new BluetoothEndPoint(BitConverter.ToUInt64(bytes[2..10]), new Guid(bytes[10..26]), BitConverter.ToUInt32(bytes[26..]));
    }

    public override string ToString() => $"({BluetoothBatteryProperty.FormatAddress(Address)}):{Port}";
}
