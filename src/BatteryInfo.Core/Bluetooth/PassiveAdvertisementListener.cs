using System.Runtime.InteropServices.WindowsRuntime;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Safety;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace BatteryInfo.Core.Bluetooth;

/// <summary>One manufacturer-data section of a received BLE advertisement.</summary>
/// <param name="Address">The advertiser's address. Phones and earbuds use random ones that change every few minutes.</param>
/// <param name="PublicAddress">The address is the device's public (identity) address rather than a random one.</param>
/// <param name="Rssi">Signal strength in dBm; -127 means Windows did not measure it.</param>
/// <param name="Kind">The advertisement type Windows reports, e.g. "ScannableUndirected" (for traces).</param>
/// <param name="Data">The section's bytes after its 2-byte company id.</param>
public sealed record BleAdvertisement(ulong Address, bool PublicAddress, short Rssi, string Kind, byte[] Data);

/// <summary>Receives the BLE advertisements that carry one company's manufacturer data. It only ever scans passively:
/// the radio listens and never transmits, so no scan request, connection or other packet reaches any device. An
/// active scan would send a scan request to every scannable advertiser, earbuds included.</summary>
internal sealed class PassiveAdvertisementListener(ushort companyId) : IDisposable
{
    private readonly object _gate = new();
    private BluetoothLEAdvertisementWatcher? _watcher;
    private bool _disposed;

    /// <summary>Raised on a thread-pool thread for every section of <c>companyId</c> in a received advertisement.</summary>
    public event EventHandler<BleAdvertisement>? Received;

    /// <summary>Raised when the scan stops without being asked to (radio turned off, ...), with Windows' reason.</summary>
    public event EventHandler<string>? Failed;

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _watcher is not null;
        }
    }

    /// <summary>A watcher set to scan passively; Passive is also Windows' default, set here so it never depends on it.</summary>
    internal static BluetoothLEAdvertisementWatcher CreateWatcher() => new() { ScanningMode = BluetoothLEScanningMode.Passive };

    /// <summary>Throws <see cref="UnsafeCommandException"/> (and logs a warning) unless the watcher scans passively.</summary>
    internal static void EnsurePassive(BluetoothLEAdvertisementWatcher watcher)
    {
        if (watcher.ScanningMode == BluetoothLEScanningMode.Passive) return;
        const string message = "Refused to start a Bluetooth LE scan that is not passive: it would send scan requests to devices";
        Log.Warn(message);
        throw new UnsafeCommandException(message);
    }

    /// <summary>The data of every manufacturer section of <paramref name="company"/>, each without the company id.</summary>
    internal static IReadOnlyList<byte[]> Sections(BluetoothLEAdvertisement advertisement, ushort company) =>
        advertisement.ManufacturerData.Where(m => m.CompanyId == company).Select(m => m.Data.ToArray()).ToList();

    /// <summary>Starts listening unless it already is. After <see cref="Failed"/>, this starts a new watcher.</summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watcher is not null) return;

            var watcher = CreateWatcher();
            EnsurePassive(watcher);
            watcher.Received += OnReceived;
            watcher.Stopped += OnStopped;
            try
            {
                watcher.Start();
            }
            catch
            {
                Detach(watcher);
                throw;
            }
            _watcher = watcher;
        }
    }

    public void Stop()
    {
        BluetoothLEAdvertisementWatcher? watcher;
        lock (_gate)
        {
            watcher = _watcher;
            _watcher = null;
        }
        if (watcher is null) return;

        // Unsubscribed first, so the Stopped event of a requested stop is not reported as a failure.
        Detach(watcher);
        try
        {
            watcher.Stop();
        }
        catch (Exception e)
        {
            Log.Warn($"Stopping the Bluetooth LE scan failed ({e.Message})");
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Stop();
    }

    private void Detach(BluetoothLEAdvertisementWatcher watcher)
    {
        watcher.Received -= OnReceived;
        watcher.Stopped -= OnStopped;
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        try
        {
            foreach (var data in Sections(args.Advertisement, companyId))
            {
                Received?.Invoke(this, new BleAdvertisement(args.BluetoothAddress, args.BluetoothAddressType == BluetoothAddressType.Public,
                    args.RawSignalStrengthInDBm, args.AdvertisementType.ToString(), data));
            }
        }
        catch (Exception e)
        {
            Log.Error("Handling a Bluetooth LE advertisement failed", e);
        }
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_watcher, sender)) return;
            _watcher = null;
        }
        Detach(sender);
        var reason = args.Error == BluetoothError.Success ? "stopped" : args.Error.ToString();
        Log.Warn($"The Bluetooth LE scan stopped on its own ({reason})");
        try
        {
            Failed?.Invoke(this, reason);
        }
        catch (Exception e)
        {
            Log.Error("A Bluetooth LE scan listener failed", e);
        }
    }
}
