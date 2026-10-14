using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Safety;

namespace BatteryInfo.Core.Vendors.Huawei;

/// <summary>The HUAWEI earbuds allow-list: the only frames that may ever be written to their SPP socket. It works on
/// raw bytes rather than being an <see cref="ICommandPolicy"/>, because there is no HID device on this path.</summary>
/// <remarks>Only the battery request (service 0x01, command 0x08) is a known pure read (docs/protocols/huawei.md §6);
/// other OpenFreebuds commands (0x2Bxx, 0x011F, 0x0125, ...) are often writes. The frames are listed byte for byte
/// here, independent of <see cref="HuaweiProtocol.BuildFrame"/>, so a builder bug cannot widen the list.</remarks>
public static class HuaweiCommandPolicy
{
    private static readonly byte[][] Allowed =
    [
        // Empty TLVs 1, 2, 3 (OpenFreebuds; what the provider sends).
        [0x5A, 0x00, 0x09, 0x00, 0x01, 0x08, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0xFB, 0xB9],

        // Empty TLV 1 only (Gadgetbridge); proven to return the same reply.
        [0x5A, 0x00, 0x05, 0x00, 0x01, 0x08, 0x01, 0x00, 0xF5, 0x3D],
    ];

    /// <summary>True only when <paramref name="frame"/> is exactly one allowed battery request.</summary>
    public static bool IsAllowed(ReadOnlySpan<byte> frame)
    {
        foreach (var allowed in Allowed)
        {
            if (frame.SequenceEqual(allowed)) return true;
        }
        return false;
    }

    /// <summary>Throws <see cref="UnsafeCommandException"/> (and logs a warning) unless the frame is allowed.</summary>
    public static void EnsureAllowed(ReadOnlySpan<byte> frame)
    {
        if (IsAllowed(frame)) return;
        var message = $"Refused to send a frame not on the HUAWEI allow-list: {Log.Hex(frame[..Math.Min(frame.Length, 16)])}";
        Log.Warn(message);
        throw new UnsafeCommandException(message);
    }
}

/// <summary>An SPP stream whose every write is checked by <see cref="HuaweiCommandPolicy"/> first: a refused frame
/// throws <see cref="UnsafeCommandException"/> and nothing is sent. Each write must carry exactly one whole frame.
/// Reads pass straight through. Disposing it disposes the inner stream.</summary>
internal sealed class GuardedSppStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        HuaweiCommandPolicy.EnsureAllowed(buffer);
        inner.Write(buffer);
    }

    public override void WriteByte(byte value) => Write([value]);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        HuaweiCommandPolicy.EnsureAllowed(buffer.Span);
        return inner.WriteAsync(buffer, cancellationToken);
    }

    public override void Flush() => inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
