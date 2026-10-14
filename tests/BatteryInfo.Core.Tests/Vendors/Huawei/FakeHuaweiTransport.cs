using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Huawei;

namespace BatteryInfo.Core.Tests.Vendors.Huawei;

/// <summary>A fake Bluetooth side: which earbuds are paired and connected, what Windows' HFP value is, and the SPP
/// stream an open hands back.</summary>
internal sealed class FakeHuaweiTransport : IHuaweiTransport
{
    public List<HuaweiDevice> Paired { get; } = [];

    public HashSet<ulong> Connected { get; } = [];

    public Dictionary<ulong, int?> WindowsBattery { get; } = [];

    /// <summary>What OpenSpp returns (or throws) for an address. Unset: the open fails as if nothing answered.</summary>
    public Func<ulong, Stream>? OnOpen { get; set; }

    public List<ulong> Opens { get; } = [];

    public Exception? FindError { get; set; }

    public bool Disposed { get; private set; }

    public bool HasConnectionListeners => ConnectionChanged is not null;

    public event EventHandler? ConnectionChanged;

    public void RaiseConnectionChanged() => ConnectionChanged?.Invoke(this, EventArgs.Empty);

    public IReadOnlyList<HuaweiDevice> FindPaired() => FindError is null ? Paired.ToList() : throw FindError;

    public bool IsConnected(ulong address) => Connected.Contains(address);

    public Stream OpenSpp(ulong address, TimeSpan connectTimeout)
    {
        Opens.Add(address);
        return OnOpen?.Invoke(address) ?? throw new TimeoutException("fake: no stream configured");
    }

    public int? ReadWindowsBattery(ulong address) => WindowsBattery.GetValueOrDefault(address);

    public void Dispose() => Disposed = true;
}

/// <summary>A fake RFCOMM stream. Reads hand out queued chunks; with nothing queued a read advances the clock by half a
/// second and throws <see cref="TimeoutException"/>, like the Windows transport. An empty chunk means the link closed.</summary>
internal sealed class FakeSppStream(ManualTimeProvider time) : Stream
{
    private readonly Queue<byte[]> _chunks = new();

    public List<byte[]> Written { get; } = [];

    /// <summary>Chunks to deliver after each write (the earbuds' answer).</summary>
    public Func<byte[], IEnumerable<byte[]>>? OnWrite { get; set; }

    public Exception? ReadError { get; set; }

    public Exception? WriteError { get; set; }

    public bool Disposed { get; private set; }

    public void Deliver(params byte[][] chunks)
    {
        foreach (var chunk in chunks) _chunks.Enqueue(chunk);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (ReadError is not null) throw ReadError;
        if (!_chunks.TryDequeue(out var chunk))
        {
            time.Advance(TimeSpan.FromMilliseconds(500));
            throw new TimeoutException("fake: nothing to read");
        }
        if (chunk.Length > count)
        {
            var rest = chunk[count..];
            var remaining = _chunks.ToArray();
            _chunks.Clear();
            _chunks.Enqueue(rest);
            foreach (var r in remaining) _chunks.Enqueue(r);
            chunk = chunk[..count];
        }
        chunk.CopyTo(buffer, offset);
        return chunk.Length;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (WriteError is not null) throw WriteError;
        var copy = buffer.AsSpan(offset, count).ToArray();
        Written.Add(copy);
        if (OnWrite is not null) Deliver(OnWrite(copy).ToArray());
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
