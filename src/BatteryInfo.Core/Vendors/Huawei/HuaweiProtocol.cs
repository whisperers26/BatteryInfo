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
}
