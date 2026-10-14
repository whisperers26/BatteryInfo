namespace BatteryInfo.Core.Hid;

public enum HidAccess
{
    /// <summary>Zero desired access: feature reports and caps only (works on mouse/keyboard collections).</summary>
    None,

    /// <summary>GENERIC_READ | GENERIC_WRITE, still shared with other processes.</summary>
    ReadWrite,
}

/// <summary>A Win32 failure talking to a HID device.</summary>
public sealed class HidIoException(string message, int win32Error) : IOException($"{message} (Win32 error {win32Error})")
{
    public const int AccessDenied = 5;

    public int Win32Error { get; } = win32Error;

    /// <summary>The device went away or stopped answering: not connected, invalid handle, aborted, general
    /// failure, bad command.</summary>
    public bool DeviceGone => Win32Error is 1167 or 6 or 995 or 31 or 22;
}

/// <summary>One opened HID collection. Not thread-safe; use from one thread at a time.</summary>
public interface IHidConnection : IDisposable
{
    HidDeviceInfo Device { get; }

    /// <summary>Writes one output report (WriteFile). <paramref name="report"/> must be exactly
    /// <see cref="HidDeviceInfo.OutputReportLength"/> bytes, byte 0 being the report id.</summary>
    void Write(ReadOnlySpan<byte> report);

    /// <summary>Reads the next input report, or returns null when none arrives within <paramref name="timeout"/>.</summary>
    byte[]? Read(TimeSpan timeout);

    /// <summary>HidD_SetFeature. Byte 0 is the report id.</summary>
    void SetFeature(ReadOnlySpan<byte> report);

    /// <summary>HidD_GetFeature. On entry byte 0 holds the report id; on return the buffer holds the report.</summary>
    void GetFeature(Span<byte> report);

    /// <summary>Drops input reports already queued for this handle (HidD_FlushQueue).</summary>
    void FlushQueue();
}

public interface IHidBackend
{
    /// <summary>Lists present HID collections. Only collections whose VID/PID pass <paramref name="filter"/>
    /// are opened and queried, so providers never touch other vendors' devices.</summary>
    IReadOnlyList<HidDeviceInfo> Enumerate(Func<ushort, ushort, bool>? filter = null);

    /// <summary>Opens a collection with shared access. Throws <see cref="HidIoException"/> (5 = access denied).</summary>
    IHidConnection Open(HidDeviceInfo device, HidAccess access);
}
