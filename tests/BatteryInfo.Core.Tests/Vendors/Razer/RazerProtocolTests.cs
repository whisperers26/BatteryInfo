using BatteryInfo.Core.Vendors.Razer;

namespace BatteryInfo.Core.Tests.Vendors.Razer;

public class RazerProtocolTests
{
    // 91-byte Windows buffer: [0] report id 0, then the 90-byte Razer report (razer.md §3, §4).
    private static byte[] Expected(byte size, byte commandClass, byte commandId, byte crc)
    {
        var buffer = new byte[91];
        buffer[2] = 0x1F;
        buffer[6] = size;
        buffer[7] = commandClass;
        buffer[8] = commandId;
        buffer[89] = crc;
        return buffer;
    }

    [Fact]
    public void Battery_level_request_matches_the_spec_bytes()
    {
        // 00 | 00 1F 00 00 00 02 07 80 | 80 x 00 | 85 00
        Assert.Equal(Expected(0x02, 0x07, 0x80, crc: 0x85), RazerProtocol.BatteryLevelRequest());
    }

    [Fact]
    public void Charging_status_request_matches_the_spec_bytes()
    {
        // 00 | 00 1F 00 00 00 02 07 84 | 80 x 00 | 81 00
        Assert.Equal(Expected(0x02, 0x07, 0x84, crc: 0x81), RazerProtocol.ChargingStatusRequest());
    }

    [Fact]
    public void Wireless_status_request_matches_the_spec_bytes()
    {
        // 00 | 00 1F 00 00 00 50 00 BF | 80 x 00 | EF 00
        Assert.Equal(Expected(0x50, 0x00, 0xBF, crc: 0xEF), RazerProtocol.WirelessStatusRequest());
    }

    [Fact]
    public void Requests_are_91_bytes_with_report_id_0_and_a_zero_reserved_byte()
    {
        var request = RazerProtocol.BatteryLevelRequest();

        Assert.Equal(RazerProtocol.BufferLength, request.Length);
        Assert.Equal(91, request.Length);
        Assert.Equal(0x00, request[0]);
        Assert.Equal(0x00, request[90]);
    }

    [Fact]
    public void Build_uses_the_given_transaction_id_and_arguments()
    {
        var request = RazerProtocol.Build(0x07, 0x80, 0x02, [0x00], transactionId: 0x3F);

        Assert.Equal(0x3F, request[2]);
        Assert.Equal(0x85, request[89]); // the txid is outside the CRC
    }

    [Fact]
    public void Crc_is_the_xor_of_report_bytes_2_to_87_and_skips_status_and_txid()
    {
        var buffer = new byte[91];
        buffer[1] = 0x02; // status: excluded
        buffer[2] = 0x1F; // txid: excluded
        buffer[6] = 0x02;
        buffer[7] = 0x07;
        buffer[8] = 0x80;
        buffer[10] = 0xF5;
        buffer[88] = 0x01; // last byte covered (report offset 87)
        buffer[90] = 0xAA; // reserved: excluded

        Assert.Equal(0x02 ^ 0x07 ^ 0x80 ^ 0xF5 ^ 0x01, RazerProtocol.Crc(buffer));
    }

    [Fact]
    public void Has_valid_crc_checks_byte_89()
    {
        var request = RazerProtocol.BatteryLevelRequest();
        Assert.True(RazerProtocol.HasValidCrc(request));

        request[89] ^= 0x01;
        Assert.False(RazerProtocol.HasValidCrc(request));
    }

    [Fact]
    public void Build_rejects_more_than_80_argument_bytes()
    {
        Assert.Throws<ArgumentException>(() => RazerProtocol.Build(0x07, 0x80, 0x02, new byte[81]));
    }
}
