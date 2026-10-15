namespace BatteryInfo.Core.Vendors.Apple;

/// <summary>The plaintext fields of one Proximity Pairing status message (docs/protocols/apple.md §4-5). Levels are
/// the raw nibbles: 0-10 are tenths, anything else means not reported.</summary>
/// <param name="ModelId">The model as a PID, e.g. 0x2014 for bytes <c>14 20</c>.</param>
/// <param name="FromLeft">The frame was sent by the left pod (status bit 5).</param>
/// <param name="ThisLevel">The sending pod's level; for a one-battery model, the battery's.</param>
/// <param name="OtherLevel">The other pod's level.</param>
/// <param name="Status">Byte 5, kept for traces.</param>
/// <param name="Lid">Byte 8, kept for traces.</param>
/// <param name="State">Byte 10 (connection and audio state), kept for traces.</param>
public readonly record struct ProximityStatus(
    ushort ModelId,
    bool FromLeft,
    int ThisLevel,
    int OtherLevel,
    int CaseLevel,
    bool ThisCharging,
    bool OtherCharging,
    bool CaseCharging,
    byte Status,
    byte Lid,
    byte State)
{
    public int LeftLevel => FromLeft ? ThisLevel : OtherLevel;

    public int RightLevel => FromLeft ? OtherLevel : ThisLevel;

    public bool LeftCharging => FromLeft ? ThisCharging : OtherCharging;

    public bool RightCharging => FromLeft ? OtherCharging : ThisCharging;
}

/// <summary>Decodes Apple's Proximity Pairing broadcast (docs/protocols/apple.md). Pure: no I/O.</summary>
public static class AppleProximityProtocol
{
    public const ushort AppleCompanyId = 0x004C;

    public const byte ProximityPairingType = 0x07;

    /// <summary>The length byte of a status message: 25 more bytes, 27 in all.</summary>
    public const byte StatusLength = 0x19;

    public const byte StatusSubtype = 0x01;

    /// <summary>The bytes of the status message that are not encrypted (0-10); the rest is never decoded or logged.</summary>
    public const int PlaintextLength = 11;

    private const int MaxLevel = 10;

    /// <summary>The first status message in one Apple manufacturer-data entry (the bytes after the company id), or
    /// null when it holds none. Other Proximity Pairing frames (sub-types other than 0x01, other lengths) have other
    /// layouts and are skipped, as is everything after a message that runs past the end of the data.</summary>
    public static ProximityStatus? Parse(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        while (offset + 2 <= data.Length)
        {
            var type = data[offset];
            var length = data[offset + 1];
            if (offset + 2 + length > data.Length) return null;

            var message = data.Slice(offset, 2 + length);
            offset += 2 + length;
            if (type == ProximityPairingType && length == StatusLength && message[2] == StatusSubtype) return Decode(message);
        }
        return null;
    }

    /// <summary>A level nibble as a percent, the lower end of its 10 % step; null when it is not reported.</summary>
    public static int? Percent(int level) => level is >= 0 and <= MaxLevel ? level * 10 : null;

    private static ProximityStatus Decode(ReadOnlySpan<byte> m) => new(
        ModelId: (ushort)(m[3] | m[4] << 8),
        FromLeft: (m[5] & 0x20) != 0,
        ThisLevel: m[6] & 0x0F,
        OtherLevel: m[6] >> 4,
        CaseLevel: m[7] & 0x0F,
        ThisCharging: (m[7] & 0x10) != 0,
        OtherCharging: (m[7] & 0x20) != 0,
        CaseCharging: (m[7] & 0x40) != 0,
        Status: m[5],
        Lid: m[8],
        State: m[10]);
}
