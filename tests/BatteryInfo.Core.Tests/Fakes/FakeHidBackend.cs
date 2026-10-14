using BatteryInfo.Core.Hid;

namespace BatteryInfo.Core.Tests.Fakes;

/// <summary>One fake HID collection: an input queue the test fills, and a record of everything sent to it.</summary>
internal sealed class FakeHidDevice(HidDeviceInfo info)
{
    public HidDeviceInfo Info { get; set; } = info;

    public Queue<byte[]> Input { get; } = new();

    public List<byte[]> Written { get; } = [];

    public List<byte[]> FeaturesSet { get; } = [];

    public List<HidAccess> Opens { get; } = [];

    /// <summary>Called after each Write; typically enqueues replies on this or a sibling collection.</summary>
    public Action<byte[]>? OnWrite { get; set; }

    public Action<byte[]>? OnSetFeature { get; set; }

    /// <summary>Given the GetFeature buffer (report id in byte 0), returns the report to hand back.</summary>
    public Func<byte[], byte[]>? OnGetFeature { get; set; }

    public Exception? OpenError { get; set; }

    public Exception? ReadError { get; set; }

    public Exception? WriteError { get; set; }

    public void Enqueue(params byte[] report) => Input.Enqueue(report);

    public int SentCount => Written.Count + FeaturesSet.Count;
}

internal sealed class FakeHidBackend : IHidBackend
{
    private int _next;

    public List<FakeHidDevice> Devices { get; } = [];

    public FakeHidDevice Add(HidDeviceInfo info)
    {
        var device = new FakeHidDevice(info);
        Devices.Add(device);
        return device;
    }

    public FakeHidDevice Add(ushort vid, ushort pid, ushort usagePage, ushort usage, int input, int output,
        int feature = 0, int? mi = null, string? parent = null, string? product = null, string? serial = null,
        byte? outputReportId = null, byte? featureReportId = null, string? path = null, bool accessible = true)
    {
        var n = _next++;
        path ??= $@"\\?\hid#vid_{vid:x4}&pid_{pid:x4}{(mi is null ? "" : $"&mi_{mi:x2}")}&col{n:x2}#fake&{n}#{{4d1e55b2-f16f-11cf-88cb-001111000030}}";
        return Add(new HidDeviceInfo(path, vid, pid, 0x0100, usagePage, usage, input, output, feature, mi,
            $@"HID\VID_{vid:X4}&PID_{pid:X4}\FAKE&{n}", parent ?? $@"USB\VID_{vid:X4}&PID_{pid:X4}\FAKE",
            product, serial, outputReportId, featureReportId, accessible));
    }

    public IReadOnlyList<HidDeviceInfo> Enumerate(Func<ushort, ushort, bool>? filter = null) =>
        Devices.Select(d => d.Info).Where(i => filter?.Invoke(i.VendorId, i.ProductId) ?? true).ToList();

    public IHidConnection Open(HidDeviceInfo device, HidAccess access)
    {
        var fake = Devices.Single(d => d.Info.Path == device.Path);
        fake.Opens.Add(access);
        if (fake.OpenError is not null) throw fake.OpenError;
        return new FakeHidConnection(fake, access);
    }

    private sealed class FakeHidConnection(FakeHidDevice fake, HidAccess access) : IHidConnection
    {
        private bool _disposed;

        public HidDeviceInfo Device => fake.Info;

        public void Write(ReadOnlySpan<byte> report)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (access != HidAccess.ReadWrite) throw new HidIoException("WriteFile on a zero-access handle", 5);
            if (report.Length != fake.Info.OutputReportLength)
                throw new ArgumentException($"Output report must be {fake.Info.OutputReportLength} bytes, got {report.Length}.");
            if (fake.WriteError is not null) throw fake.WriteError;
            var copy = report.ToArray();
            fake.Written.Add(copy);
            fake.OnWrite?.Invoke(copy);
        }

        public byte[]? Read(TimeSpan timeout)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (fake.ReadError is not null) throw fake.ReadError;
            return fake.Input.TryDequeue(out var report) ? report : null;
        }

        public void SetFeature(ReadOnlySpan<byte> report)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var copy = report.ToArray();
            fake.FeaturesSet.Add(copy);
            fake.OnSetFeature?.Invoke(copy);
        }

        public void GetFeature(Span<byte> report)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (fake.OnGetFeature is null) throw new HidIoException("HidD_GetFeature", 31);
            var response = fake.OnGetFeature(report.ToArray());
            response.AsSpan(0, Math.Min(response.Length, report.Length)).CopyTo(report);
        }

        public void FlushQueue() => fake.Input.Clear();

        public void Dispose() => _disposed = true;
    }
}
