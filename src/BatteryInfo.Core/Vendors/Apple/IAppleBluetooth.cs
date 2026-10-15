using BatteryInfo.Core.Bluetooth;

namespace BatteryInfo.Core.Vendors.Apple;

/// <summary>A paired Apple audio device.</summary>
/// <param name="Address">Its classic Bluetooth address, e.g. 0x282D7FB83C8B.</param>
/// <param name="Name">Its Bluetooth name, e.g. "AirPods Pro".</param>
/// <param name="ModelId">The PID of its Device ID record, e.g. 0x2024; the same id it broadcasts.</param>
public sealed record AppleDevice(ulong Address, string Name, ushort ModelId);

/// <summary>The Bluetooth side of the Apple provider, behind an interface so the provider can be tested without a
/// radio. Nothing here sends anything to a device: the queries read what Windows already knows, and the scan is
/// passive.</summary>
public interface IAppleBluetooth : IDisposable
{
    /// <summary>Paired Apple audio devices known to Windows, connected or not.</summary>
    IReadOnlyList<AppleDevice> FindPaired();

    /// <summary>Whether Windows reports an active connection to the device right now.</summary>
    bool IsConnected(ulong address);

    /// <summary>Windows' own battery value for the device, or null (docs/protocols/apple.md §9).</summary>
    int? ReadWindowsBattery(ulong address);

    bool IsListening { get; }

    /// <summary>Starts the passive scan for Apple advertisements unless it runs.</summary>
    /// <exception cref="Exception">Windows refused to start it.</exception>
    void StartListening();

    void StopListening();

    /// <summary>Raised on a thread-pool thread for every Apple manufacturer-data section received.</summary>
    event EventHandler<BleAdvertisement>? AdvertisementReceived;

    /// <summary>Raised when the scan stops on its own, with Windows' reason (e.g. the radio was turned off).</summary>
    event EventHandler<string>? ListeningFailed;

    /// <summary>Raised when a known device connects or disconnects.</summary>
    event EventHandler? ConnectionChanged;
}
