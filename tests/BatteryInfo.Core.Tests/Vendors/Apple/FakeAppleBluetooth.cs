using BatteryInfo.Core.Bluetooth;
using BatteryInfo.Core.Vendors.Apple;

namespace BatteryInfo.Core.Tests.Vendors.Apple;

/// <summary>A fake Bluetooth side: which Apple devices are paired and connected, Windows' own battery value, and the
/// advertisements the passive scan hands over.</summary>
internal sealed class FakeAppleBluetooth : IAppleBluetooth
{
    public List<AppleDevice> Paired { get; } = [];

    public HashSet<ulong> Connected { get; } = [];

    public Dictionary<ulong, int?> WindowsBattery { get; } = [];

    /// <summary>What StartListening throws, if anything.</summary>
    public Exception? StartError { get; set; }

    public int Starts { get; private set; }

    public bool Disposed { get; private set; }

    public bool IsListening { get; private set; }

    public bool HasListeners => AdvertisementReceived is not null && ListeningFailed is not null && ConnectionChanged is not null;

    public event EventHandler<BleAdvertisement>? AdvertisementReceived;

    public event EventHandler<string>? ListeningFailed;

    public event EventHandler? ConnectionChanged;

    public IReadOnlyList<AppleDevice> FindPaired() => Paired.ToList();

    public bool IsConnected(ulong address) => Connected.Contains(address);

    public int? ReadWindowsBattery(ulong address) => WindowsBattery.GetValueOrDefault(address);

    public void StartListening()
    {
        if (IsListening) return;
        Starts++;
        if (StartError is not null) throw StartError;
        IsListening = true;
    }

    public void StopListening() => IsListening = false;

    /// <summary>Hands over one Apple advertisement; ignored like Windows would while the scan is off.</summary>
    public void Advertise(ulong address, short rssi, string hex)
    {
        if (!IsListening) return;
        AdvertisementReceived?.Invoke(this, new BleAdvertisement(address, false, rssi, "ScannableUndirected", AppleVectors.Bytes(hex)));
    }

    public void FailListening(string reason)
    {
        IsListening = false;
        ListeningFailed?.Invoke(this, reason);
    }

    public void RaiseConnectionChanged() => ConnectionChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}
