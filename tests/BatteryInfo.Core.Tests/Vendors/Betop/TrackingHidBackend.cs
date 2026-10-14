using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Tests.Fakes;

namespace BatteryInfo.Core.Tests.Vendors.Betop;

/// <summary>Wraps <see cref="FakeHidBackend"/> to count open connections and, optionally, to ignore the enumeration
/// filter the way a buggy backend would.</summary>
internal sealed class TrackingHidBackend(FakeHidBackend inner, bool ignoreFilter = false) : IHidBackend
{
    private int _open;

    /// <summary>Connections opened and not yet disposed.</summary>
    public int OpenConnections => _open;

    public List<(ushort VendorId, ushort ProductId, bool Accepted)> FilterCalls { get; } = [];

    public IReadOnlyList<HidDeviceInfo> Enumerate(Func<ushort, ushort, bool>? filter = null)
    {
        if (ignoreFilter) return inner.Enumerate();
        return inner.Enumerate((vid, pid) =>
        {
            var accepted = filter?.Invoke(vid, pid) ?? true;
            FilterCalls.Add((vid, pid, accepted));
            return accepted;
        });
    }

    public IHidConnection Open(HidDeviceInfo device, HidAccess access)
    {
        var connection = inner.Open(device, access);
        _open++;
        return new Tracked(connection, this);
    }

    private sealed class Tracked(IHidConnection inner, TrackingHidBackend owner) : IHidConnection
    {
        private bool _disposed;

        public HidDeviceInfo Device => inner.Device;

        public void Write(ReadOnlySpan<byte> report) => inner.Write(report);

        public byte[]? Read(TimeSpan timeout) => inner.Read(timeout);

        public void SetFeature(ReadOnlySpan<byte> report) => inner.SetFeature(report);

        public void GetFeature(Span<byte> report) => inner.GetFeature(report);

        public void FlushQueue() => inner.FlushQueue();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._open--;
            inner.Dispose();
        }
    }
}
