using System.Diagnostics.CodeAnalysis;

namespace BatteryInfo.Core.Vendors.Huawei;

/// <summary>HUAWEI earbuds SPP framing and battery TLVs (docs/protocols/huawei.md §5-§8). Pure: no I/O.</summary>
/// <remarks>Frame: <c>5A | LEN (u16 BE) | 00 (unsliced) | service | command | TLVs | CRC-16/XMODEM (BE)</c>.
/// LEN counts the bytes from offset 3 up to the CRC; the CRC covers everything before it, magic included.</remarks>
public static class HuaweiProtocol
{
    public const byte Magic = 0x5A;
    public const byte DeviceConfigService = 0x01;
    public const byte BatteryLevelCommand = 0x08;

    /// <summary>Magic plus the two LEN bytes.</summary>
    private const int HeaderLength = 3;

    /// <summary>Bytes of a frame not counted in LEN: magic, LEN and CRC.</summary>
    private const int Overhead = 5;

    /// <summary>Largest LEN accepted. Earbud frames are under 64 bytes, so a larger LEN means a stray 0x5A.</summary>
    internal const int MaxLength = 0x400;

    /// <summary>CRC-16/XMODEM: poly 0x1021, init 0, no reflection, no final xor.</summary>
    public static ushort Crc16Xmodem(ReadOnlySpan<byte> data)
    {
        var crc = 0;
        foreach (var b in data)
        {
            crc ^= b << 8;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1;
            }
        }
        return (ushort)crc;
    }

    /// <summary>An unsliced frame carrying <paramref name="tlvs"/> (already encoded) for one service and command.</summary>
    public static byte[] BuildFrame(byte service, byte command, ReadOnlySpan<byte> tlvs)
    {
        var length = 3 + tlvs.Length; // slice flag, service, command, TLVs
        if (length > ushort.MaxValue) throw new ArgumentException("The TLVs do not fit in one frame.", nameof(tlvs));

        var frame = new byte[length + Overhead];
        frame[0] = Magic;
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = 0x00;
        frame[4] = service;
        frame[5] = command;
        tlvs.CopyTo(frame.AsSpan(6));
        var crc = Crc16Xmodem(frame.AsSpan(0, HeaderLength + length));
        frame[HeaderLength + length] = (byte)(crc >> 8);
        frame[HeaderLength + length + 1] = (byte)crc;
        return frame;
    }

    /// <summary>The battery request, service 0x01 command 0x08 with empty TLVs 1, 2 and 3:
    /// <c>5A 00 09 00 01 08 01 00 02 00 03 00 FB B9</c>. The only frame the app ever sends to the earbuds.</summary>
    public static byte[] BuildBatteryRequest() =>
        BuildFrame(DeviceConfigService, BatteryLevelCommand, [0x01, 0x00, 0x02, 0x00, 0x03, 0x00]);

    /// <summary>Decodes the frame at the start of <paramref name="data"/>. <paramref name="frameLength"/> is the
    /// number of bytes the frame occupies when the result is <see cref="DecodeResult.Complete"/> or
    /// <see cref="DecodeResult.Sliced"/>.</summary>
    internal static DecodeResult Decode(ReadOnlySpan<byte> data, out HuaweiFrame? frame, out int frameLength)
    {
        frame = null;
        frameLength = 0;
        if (data.IsEmpty || data[0] != Magic) return DecodeResult.Invalid;
        if (data.Length < HeaderLength) return DecodeResult.Incomplete;

        var length = (data[1] << 8) | data[2];
        if (length < 3 || length > MaxLength) return DecodeResult.Invalid;
        if (data.Length < length + Overhead) return DecodeResult.Incomplete;

        var crc = (ushort)((data[HeaderLength + length] << 8) | data[HeaderLength + length + 1]);
        if (Crc16Xmodem(data[..(HeaderLength + length)]) != crc) return DecodeResult.Invalid;

        frameLength = length + Overhead;
        if (data[3] != 0x00) return DecodeResult.Sliced;
        frame = new HuaweiFrame(data[4], data[5], data[6..(HeaderLength + length)].ToArray(), data[..frameLength].ToArray());
        return DecodeResult.Complete;
    }

    internal enum DecodeResult
    {
        /// <summary>A whole unsliced frame with a good CRC.</summary>
        Complete,

        /// <summary>A whole frame with a good CRC that is one slice of a longer message (never seen from earbuds; dropped).</summary>
        Sliced,

        /// <summary>Looks like a frame so far; more bytes are needed.</summary>
        Incomplete,

        /// <summary>Not a frame at this position: wrong magic, impossible length or bad CRC.</summary>
        Invalid,
    }
}

/// <summary>One complete, CRC-checked, unsliced frame.</summary>
/// <param name="Payload">The TLV bytes after the service and command ids.</param>
/// <param name="Raw">The whole frame, magic to CRC.</param>
public sealed record HuaweiFrame(byte Service, byte Command, byte[] Payload, byte[] Raw);

/// <summary>Cuts an SPP byte stream into frames. Bytes before a magic byte are skipped; a candidate frame with a
/// bad CRC or an impossible length is dropped one byte at a time until the stream lines up again (§8).</summary>
public sealed class HuaweiFrameReader
{
    private byte[] _buffer = new byte[256];
    private int _count;

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_count + data.Length > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _count + data.Length));
        }
        data.CopyTo(_buffer.AsSpan(_count));
        _count += data.Length;
    }

    public bool TryRead([NotNullWhen(true)] out HuaweiFrame? frame)
    {
        while (true)
        {
            var start = _buffer.AsSpan(0, _count).IndexOf(HuaweiProtocol.Magic);
            if (start < 0)
            {
                _count = 0;
                frame = null;
                return false;
            }
            Discard(start);

            switch (HuaweiProtocol.Decode(_buffer.AsSpan(0, _count), out frame, out var length))
            {
                case HuaweiProtocol.DecodeResult.Complete:
                    Discard(length);
                    return frame is not null;
                case HuaweiProtocol.DecodeResult.Sliced:
                    Discard(length);
                    continue;
                case HuaweiProtocol.DecodeResult.Invalid:
                    Discard(1);
                    continue;
                default:
                    // Incomplete. If the candidate is a stray 0x5A claiming a long length, a real frame after it
                    // would wait behind it until enough bytes arrive; take a complete, CRC-valid later frame instead.
                    var later = FindCompleteFrameAfterStart();
                    if (later < 0) return false;
                    Discard(later);
                    continue;
            }
        }
    }

    private int FindCompleteFrameAfterStart()
    {
        var data = _buffer.AsSpan(0, _count);
        for (var i = 1; i < data.Length; i++)
        {
            if (data[i] != HuaweiProtocol.Magic) continue;
            if (HuaweiProtocol.Decode(data[i..], out _, out _) is HuaweiProtocol.DecodeResult.Complete or HuaweiProtocol.DecodeResult.Sliced)
            {
                return i;
            }
        }
        return -1;
    }

    private void Discard(int count)
    {
        if (count == 0) return;
        _buffer.AsSpan(count, _count - count).CopyTo(_buffer);
        _count -= count;
    }
}
