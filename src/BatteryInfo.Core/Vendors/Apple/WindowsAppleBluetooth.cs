using System.Globalization;
using BatteryInfo.Core.Bluetooth;
using BatteryInfo.Core.Interop;

namespace BatteryInfo.Core.Vendors.Apple;

/// <summary>The real Bluetooth side of the Apple provider: paired devices and the connected check from the Win32
/// Bluetooth API (cached records only, no inquiry or paging), their model from the BTHENUM instance ids, Windows' own
/// battery property, WinRT connection-status events, and a passive scan for Apple advertisements.</summary>
public sealed class WindowsAppleBluetooth : IAppleBluetooth
{
    private const int AudioVideoMajorClass = 0x04;

    /// <summary>Vendor id source 1 (Bluetooth SIG), vendor 0x004C (Apple), then the 4 hex digits of the PID.</summary>
    private const string AppleDeviceId = "_VID&0001004C_PID&";

    private readonly BluetoothConnectionWatcher _watcher = new("Apple");
    private readonly PassiveAdvertisementListener _listener = new(AppleProximityProtocol.AppleCompanyId);

    public event EventHandler<BleAdvertisement>? AdvertisementReceived
    {
        add => _listener.Received += value;
        remove => _listener.Received -= value;
    }

    public event EventHandler<string>? ListeningFailed
    {
        add => _listener.Failed += value;
        remove => _listener.Failed -= value;
    }

    public event EventHandler? ConnectionChanged
    {
        add => _watcher.Changed += value;
        remove => _watcher.Changed -= value;
    }

    public bool IsListening => _listener.IsRunning;

    /// <summary>Paired devices of the audio/video class whose Device ID record is Apple's (docs/protocols/apple.md §2).</summary>
    public IReadOnlyList<AppleDevice> FindPaired()
    {
        var paired = BluetoothApis.EnumeratePaired();
        if (paired.Count == 0) return [];

        var instanceIds = CfgMgr32.GetPresentDeviceIds("BTHENUM");
        var found = new List<AppleDevice>();
        foreach (var device in paired)
        {
            if (((device.ClassOfDevice >> 8) & 0x1F) != AudioVideoMajorClass) continue;
            if (AppleModelId(instanceIds, device.Address) is not { } modelId) continue;
            found.Add(new AppleDevice(device.Address, device.Name, modelId));
            _watcher.Watch(device.Address);
        }
        return found;
    }

    public bool IsConnected(ulong address) => BluetoothApis.GetDeviceInfo(address) is { Connected: true };

    public int? ReadWindowsBattery(ulong address) => BluetoothBatteryProperty.Read(address);

    public void StartListening() => _listener.Start();

    public void StopListening() => _listener.Stop();

    public void Dispose()
    {
        _listener.Dispose();
        _watcher.Dispose();
    }

    /// <summary>The PID of Apple's Device ID record on a BTHENUM service node of this address, as in
    /// BTHENUM\{0000110B-...}_VID&amp;0001004C_PID&amp;2024\7&amp;3549F023&amp;2&amp;282D7FB83C8B_C00000000, or null.</summary>
    internal static ushort? AppleModelId(IEnumerable<string> instanceIds, ulong address)
    {
        var hex = BluetoothBatteryProperty.FormatAddress(address);
        foreach (var id in instanceIds)
        {
            if (!id.Contains(hex, StringComparison.OrdinalIgnoreCase)) continue;
            var at = id.IndexOf(AppleDeviceId, StringComparison.OrdinalIgnoreCase);
            if (at < 0 || at + AppleDeviceId.Length + 4 > id.Length) continue;
            if (ushort.TryParse(id.AsSpan(at + AppleDeviceId.Length, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var pid))
            {
                return pid;
            }
        }
        return null;
    }
}
