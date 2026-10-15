using BatteryInfo.Core.Diagnostics;
using Windows.Devices.Bluetooth;

namespace BatteryInfo.Core.Bluetooth;

/// <summary>Raises <see cref="Changed"/> when a watched classic Bluetooth device connects or disconnects, from WinRT
/// connection-status events. Creating the BluetoothDevice reads Windows' record of it; it does not connect to it.</summary>
/// <param name="owner">The vendor name its log lines start with, e.g. "HUAWEI".</param>
internal sealed class BluetoothConnectionWatcher(string owner) : IDisposable
{
    private static readonly TimeSpan WatchTimeout = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly Dictionary<ulong, BluetoothDevice?> _watched = [];
    private bool _disposed;

    public event EventHandler? Changed;

    /// <summary>Subscribes once per device; later calls for the same address do nothing.</summary>
    public void Watch(ulong address)
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
            Log.Warn($"{owner}: cannot watch Bluetooth connection changes of {BluetoothBatteryProperty.FormatAddress(address)} ({e.Message})");
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
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
