using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Razer;

namespace BatteryInfo.Core.Tests.Vendors.Razer;

/// <summary>Produces what one HidD_GetFeature returns, given the request last set.</summary>
internal delegate byte[] RazerReply(FakeRazerMailbox mailbox, byte[] request);

internal static class Replies
{
    /// <summary>Status 0x02 with the mailbox's current data for the request's command.</summary>
    public static readonly RazerReply Ok = (m, r) => m.Success(r);

    public static readonly RazerReply New = Echo(0x00);
    public static readonly RazerReply Busy = Echo(0x01);
    public static readonly RazerReply Failure = Echo(0x03);
    public static readonly RazerReply NoResponse = Echo(0x04);
    public static readonly RazerReply NotSupported = Echo(0x05);

    public static readonly RazerReply BadCrc = (m, r) =>
    {
        var reply = m.Success(r);
        reply[89] ^= 0xFF;
        return reply;
    };

    /// <summary>The request header echoed back with <paramref name="status"/>.</summary>
    public static RazerReply Echo(byte status) => (_, r) =>
    {
        var reply = r.ToArray();
        reply[1] = status;
        return reply;
    };

    /// <summary>Somebody else's successful reply (Synapse shares the MI_00 mailbox).</summary>
    public static RazerReply Foreign(byte commandClass, byte commandId, byte size, byte transactionId, params byte[] args) =>
        (_, _) => Make(0x02, transactionId, commandClass, commandId, size, args);

    public static byte[] Make(byte status, byte transactionId, byte commandClass, byte commandId, byte size, byte[] args)
    {
        var reply = RazerProtocol.Build(commandClass, commandId, size, args, transactionId);
        reply[1] = status;
        return reply;
    }
}

/// <summary>The MI_00 feature-report mailbox of a Razer mouse or dongle on a <see cref="FakeHidDevice"/>.
/// By default it answers every request with status 0x02; <see cref="Script"/> overrides the next attempts of
/// one command.</summary>
internal sealed class FakeRazerMailbox
{
    private readonly Dictionary<byte, Queue<RazerReply[]>> _scripts = [];
    private RazerReply[] _current = [];
    private int _poll;
    private byte[] _lastRequest = new byte[91];

    public FakeRazerMailbox(FakeHidDevice device)
    {
        Device = device;
        device.OnSetFeature = OnSetFeature;
        device.OnGetFeature = OnGetFeature;
    }

    public FakeHidDevice Device { get; }

    public byte BatteryRaw { get; set; } = 245; // 96 %

    public byte Charging { get; set; } = 1;

    /// <summary>00/BF arguments: count, then (status, pidHi, pidLo) per slot.</summary>
    public byte[] LinkSlots { get; set; } = [0x02, 0x01, 0x00, 0xD7, 0x00, 0xFF, 0xFF];

    public Exception? SetFeatureError { get; set; }

    public Exception? GetFeatureError { get; set; }

    public int GetFeatureCalls { get; private set; }

    /// <summary>The command id of every frame sent, in order.</summary>
    public List<byte> SentIds => Device.FeaturesSet.Select(f => f[8]).ToList();

    public int SentCount(byte commandId) => SentIds.Count(id => id == commandId);

    /// <summary>For the next SetFeature of <paramref name="commandId"/>: the n-th GetFeature after it returns
    /// <paramref name="polls"/>[n], and the last entry repeats.</summary>
    public FakeRazerMailbox Script(byte commandId, params RazerReply[] polls)
    {
        if (!_scripts.TryGetValue(commandId, out var queue)) _scripts[commandId] = queue = new();
        queue.Enqueue(polls);
        return this;
    }

    /// <summary>Scripts <paramref name="attempts"/> consecutive sends of <paramref name="commandId"/> alike.</summary>
    public FakeRazerMailbox ScriptEvery(byte commandId, int attempts, params RazerReply[] polls)
    {
        for (var i = 0; i < attempts; i++) Script(commandId, polls);
        return this;
    }

    public byte[] Success(byte[] request)
    {
        byte[] args = request[8] switch
        {
            0x80 => [0x00, BatteryRaw],
            0x84 => [0x00, Charging],
            0xBF => LinkSlots,
            _ => [],
        };
        return Replies.Make(0x02, request[2], request[7], request[8], request[6], args);
    }

    private void OnSetFeature(byte[] request)
    {
        if (SetFeatureError is not null)
        {
            Device.FeaturesSet.Remove(request);
            throw SetFeatureError;
        }
        _lastRequest = request;
        _poll = 0;
        _current = _scripts.TryGetValue(request[8], out var queue) && queue.TryDequeue(out var script) ? script : [Replies.Ok];
    }

    private byte[] OnGetFeature(byte[] buffer)
    {
        GetFeatureCalls++;
        if (GetFeatureError is not null) throw GetFeatureError;
        return _current[Math.Min(_poll++, _current.Length - 1)](this, _lastRequest);
    }
}

/// <summary>Wraps a backend to count connections that are still open and how many were open at once.</summary>
internal sealed class TrackingHidBackend(IHidBackend inner) : IHidBackend
{
    private int _open;
    private int _maxOpen;

    public int OpenConnections => Volatile.Read(ref _open);

    public int MaxConcurrentConnections => Volatile.Read(ref _maxOpen);

    public IReadOnlyList<HidDeviceInfo> Enumerate(Func<ushort, ushort, bool>? filter = null) => inner.Enumerate(filter);

    public IHidConnection Open(HidDeviceInfo device, HidAccess access)
    {
        var connection = inner.Open(device, access);
        var open = Interlocked.Increment(ref _open);
        int max;
        while (open > (max = Volatile.Read(ref _maxOpen)) && Interlocked.CompareExchange(ref _maxOpen, open, max) != max)
        {
        }
        return new Tracked(connection, this);
    }

    private sealed class Tracked(IHidConnection inner, TrackingHidBackend owner) : IHidConnection
    {
        private int _disposed;

        public HidDeviceInfo Device => inner.Device;

        public void Write(ReadOnlySpan<byte> report) => inner.Write(report);

        public byte[]? Read(TimeSpan timeout) => inner.Read(timeout);

        public void SetFeature(ReadOnlySpan<byte> report) => inner.SetFeature(report);

        public void GetFeature(Span<byte> report) => inner.GetFeature(report);

        public void FlushQueue() => inner.FlushQueue();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            Interlocked.Decrement(ref owner._open);
            inner.Dispose();
        }
    }
}
