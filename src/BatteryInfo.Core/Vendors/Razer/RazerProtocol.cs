namespace BatteryInfo.Core.Vendors.Razer;

/// <summary>The Razer 90-byte feature report (docs/protocols/razer.md §3-4), carried in a 91-byte Windows buffer
/// whose byte 0 is report id 0. Pure builders and parsers; no I/O.</summary>
public static class RazerProtocol
{
    /// <summary>The Razer report proper.</summary>
    public const int ReportLength = 90;

    /// <summary>The HidD_SetFeature/HidD_GetFeature buffer: report id 0 + the 90-byte report.</summary>
    public const int BufferLength = ReportLength + 1;

    public const int ArgumentCount = 80;

    /// <summary>The transaction id openrazer, OpenRGB and razer-battery-report use for 00D6/00D7.</summary>
    public const byte DefaultTransactionId = 0x1F;

    // Indexes in the 91-byte buffer (razer.md §11).
    public const int StatusIndex = 1;
    public const int TransactionIdIndex = 2;
    public const int RemainingIndex = 3; // big-endian, 2 bytes
    public const int ProtocolTypeIndex = 5;
    public const int DataSizeIndex = 6;
    public const int ClassIndex = 7;
    public const int IdIndex = 8;
    public const int ArgumentsIndex = 9;
    public const int CrcIndex = 89;
    public const int ReservedIndex = 90;

    public const byte BatteryClass = 0x07;
    public const byte BatteryLevelId = 0x80;
    public const byte ChargingStatusId = 0x84;
    public const byte BatteryDataSize = 0x02;
    public const byte DeviceClass = 0x00;
    public const byte WirelessStatusId = 0xBF;
    public const byte WirelessStatusDataSize = 0x50;

    /// <summary>Get Battery Level: 07/80, size 2, battery id 0.</summary>
    public static byte[] BatteryLevelRequest(byte transactionId = DefaultTransactionId) =>
        Build(BatteryClass, BatteryLevelId, BatteryDataSize, [], transactionId);

    /// <summary>Get Device Charging Status: 07/84, size 2, arg0 0.</summary>
    public static byte[] ChargingStatusRequest(byte transactionId = DefaultTransactionId) =>
        Build(BatteryClass, ChargingStatusId, BatteryDataSize, [], transactionId);

    /// <summary>Get Multiple Device Wireless Connection Status V2: 00/BF, size 0x50, answered by the dongle.</summary>
    public static byte[] WirelessStatusRequest(byte transactionId = DefaultTransactionId) =>
        Build(DeviceClass, WirelessStatusId, WirelessStatusDataSize, [], transactionId);

    /// <summary>A 91-byte host request: status 0, no remaining packets, protocol 0, CRC filled in.</summary>
    public static byte[] Build(byte commandClass, byte commandId, byte dataSize, ReadOnlySpan<byte> arguments,
        byte transactionId = DefaultTransactionId)
    {
        if (arguments.Length > ArgumentCount)
            throw new ArgumentException($"A Razer report has {ArgumentCount} argument bytes, got {arguments.Length}.", nameof(arguments));

        var buffer = new byte[BufferLength];
        buffer[TransactionIdIndex] = transactionId;
        buffer[DataSizeIndex] = dataSize;
        buffer[ClassIndex] = commandClass;
        buffer[IdIndex] = commandId;
        arguments.CopyTo(buffer.AsSpan(ArgumentsIndex));
        buffer[CrcIndex] = Crc(buffer);
        return buffer;
    }

    /// <summary>XOR of report bytes 2..87 (buffer[3..88]); status, txid and the reserved byte are excluded.</summary>
    public static byte Crc(ReadOnlySpan<byte> buffer)
    {
        byte crc = 0;
        foreach (var b in buffer[RemainingIndex..CrcIndex]) crc ^= b;
        return crc;
    }

    public static bool HasValidCrc(ReadOnlySpan<byte> buffer) =>
        buffer.Length == BufferLength && buffer[CrcIndex] == Crc(buffer);

    /// <summary>What a GetFeature buffer says about <paramref name="request"/>. A reply is ours only when its
    /// txid, class, id and data size match, and for class 0x07 its first argument (the battery id) too; anything
    /// else in the shared mailbox (Synapse uses txid 0x1F as well) is <see cref="RazerReplyKind.Foreign"/>.</summary>
    public static RazerReplyKind Classify(ReadOnlySpan<byte> request, ReadOnlySpan<byte> reply)
    {
        if (!IsReplyTo(request, reply)) return RazerReplyKind.Foreign;
        return reply[StatusIndex] switch
        {
            0x02 => HasValidCrc(reply) ? RazerReplyKind.Success : RazerReplyKind.BadCrc,
            0x03 => RazerReplyKind.Failure,
            0x04 => RazerReplyKind.NoResponse,
            0x05 => RazerReplyKind.NotSupported,
            _ => RazerReplyKind.Pending, // 0x00 new, 0x01 busy, or a status this code does not know yet
        };
    }

    private static bool IsReplyTo(ReadOnlySpan<byte> request, ReadOnlySpan<byte> reply)
    {
        if (request.Length != BufferLength || reply.Length != BufferLength || reply[0] != request[0]) return false;
        if (reply[TransactionIdIndex] != request[TransactionIdIndex]
            || reply[ClassIndex] != request[ClassIndex]
            || reply[IdIndex] != request[IdIndex]
            || reply[DataSizeIndex] != request[DataSizeIndex])
        {
            return false;
        }
        return request[ClassIndex] != BatteryClass || reply[ArgumentsIndex] == request[ArgumentsIndex];
    }
}

/// <summary>How a GetFeature buffer relates to the request just sent.</summary>
public enum RazerReplyKind
{
    /// <summary>Another command's report in the shared mailbox: keep polling.</summary>
    Foreign,

    /// <summary>Ours, still new (0x00) or busy (0x01): keep polling, do not resend.</summary>
    Pending,

    /// <summary>Ours, 0x02 with a valid CRC: parse it.</summary>
    Success,

    /// <summary>Ours, 0x02 but the CRC does not match: poll again.</summary>
    BadCrc,

    /// <summary>Ours, 0x03 command failure.</summary>
    Failure,

    /// <summary>Ours, 0x04: the mouse did not answer the dongle (asleep, off or out of range).</summary>
    NoResponse,

    /// <summary>Ours, 0x05: the command is not supported.</summary>
    NotSupported,
}
