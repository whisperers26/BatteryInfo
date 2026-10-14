using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Hid;

namespace BatteryInfo.Core.Safety;

/// <summary>The only way providers send to a HID device: every output and feature report is checked
/// against the vendor's allow-list first, and a refused frame is never sent.</summary>
public sealed class GuardedHidConnection(IHidConnection inner, ICommandPolicy policy) : IHidConnection
{
    public HidDeviceInfo Device => inner.Device;

    public void Write(ReadOnlySpan<byte> report)
    {
        Check(report);
        inner.Write(report);
    }

    public byte[]? Read(TimeSpan timeout) => inner.Read(timeout);

    public void SetFeature(ReadOnlySpan<byte> report)
    {
        Check(report);
        inner.SetFeature(report);
    }

    // GET_REPORT only reads; it carries no command.
    public void GetFeature(Span<byte> report) => inner.GetFeature(report);

    public void FlushQueue() => inner.FlushQueue();

    public void Dispose() => inner.Dispose();

    private void Check(ReadOnlySpan<byte> report)
    {
        if (policy.IsAllowed(inner.Device, report)) return;
        var message = $"Refused to send a frame not on the allow-list to {inner.Device.Label}: {Log.Hex(report[..Math.Min(report.Length, 16)])}";
        Log.Warn(message);
        throw new UnsafeCommandException(message);
    }
}
