namespace BatteryInfo.Core.Vendors.Huawei;

/// <summary>A paired HUAWEI earbuds device.</summary>
/// <param name="Address">Classic Bluetooth address, e.g. 0xC0DA5EA83EC7.</param>
/// <param name="Name">Its Bluetooth name, e.g. "HUAWEI FreeClip 2".</param>
public sealed record HuaweiDevice(ulong Address, string Name);

/// <summary>The Bluetooth operations the HUAWEI provider needs, behind an interface so the provider can be tested
/// without a radio. None of the queries may page or inquire: they read what Windows already knows.</summary>
public interface IHuaweiTransport : IDisposable
{
    /// <summary>Paired HUAWEI earbuds known to Windows, connected or not.</summary>
    IReadOnlyList<HuaweiDevice> FindPaired();

    /// <summary>Whether Windows reports an active connection to the device right now.</summary>
    bool IsConnected(ulong address);

    /// <summary>Opens an RFCOMM link to the device's SerialPort service (UUID 0x1101).</summary>
    /// <returns>A stream whose reads block for at most about half a second and then throw
    /// <see cref="TimeoutException"/>; a read returning 0 means the link closed.</returns>
    /// <exception cref="TimeoutException">The connect took longer than <paramref name="connectTimeout"/>.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">The channel could not be opened (10048: another client
    /// already holds it).</exception>
    Stream OpenSpp(ulong address, TimeSpan connectTimeout);

    /// <summary>Windows' own battery value for the device (from HFP: coarse, often a few points high), or null.</summary>
    int? ReadWindowsBattery(ulong address);

    /// <summary>Raised when a known device connects or disconnects.</summary>
    event EventHandler? ConnectionChanged;
}
