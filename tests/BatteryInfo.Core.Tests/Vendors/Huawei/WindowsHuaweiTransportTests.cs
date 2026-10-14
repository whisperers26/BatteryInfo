using System.Net.Sockets;
using BatteryInfo.Core.Vendors.Huawei;
using static BatteryInfo.Core.Tests.Vendors.Huawei.HuaweiProtocolTests;

namespace BatteryInfo.Core.Tests.Vendors.Huawei;

public class WindowsHuaweiTransportTests
{
    private const ulong Address = 0xC0DA5EA83EC7;
    private static readonly Guid SerialPort = new("00001101-0000-1000-8000-00805F9B34FB");

    [Fact]
    public void Endpoint_serializes_to_sockaddr_bth_with_the_serial_port_uuid_and_port_0()
    {
        var address = new BluetoothEndPoint(Address, SerialPort, 0).Serialize();

        var bytes = Enumerable.Range(0, address.Size).Select(i => address[i]).ToArray();
        Assert.Equal(
            Bytes("20 00 | C7 3E A8 5E DA C0 00 00 | 01 11 00 00 00 00 00 10 80 00 00 80 5F 9B 34 FB | 00 00 00 00".Replace("| ", "", StringComparison.Ordinal)),
            bytes);
        Assert.Equal((AddressFamily)32, address.Family);
    }

    [Fact]
    public void Endpoint_round_trips_through_a_socket_address()
    {
        var original = new BluetoothEndPoint(Address, Guid.Empty, 1);

        var copy = Assert.IsType<BluetoothEndPoint>(original.Create(original.Serialize()));

        Assert.Equal(Address, copy.Address);
        Assert.Equal(Guid.Empty, copy.ServiceClassId);
        Assert.Equal(1u, copy.Port);
    }

    [Fact]
    public void Native_structs_have_the_win32_sizes()
    {
        if (!Environment.Is64BitProcess) return; // the sizes below are the x64 ones

        Assert.Equal(560, WindowsHuaweiTransport.DeviceInfoSize);
        Assert.Equal(40, WindowsHuaweiTransport.SearchParamsSize);
    }

    [Theory]
    [InlineData("HUAWEI FreeClip 2", 0x240418u, false, true)]
    [InlineData("HUAWEI FreeBuds Pro 3", 0x240404u, false, true)]
    [InlineData("HUAWEI FreeLace Pro", 0u, false, true)]
    [InlineData("huawei freeclip 2", 0x240418u, false, true)]
    [InlineData("Renamed buds", 0x240418u, true, true)]
    [InlineData("HUAWEI P60 Pro", 0x5A020Cu, true, false)]
    [InlineData("HUAWEI WATCH GT 4", 0x000704u, true, false)]
    [InlineData("Galaxy Buds2", 0x240404u, false, false)]
    [InlineData("", 0x240418u, false, false)]
    public void Matches_huawei_earbuds_by_name_or_by_vendor_id_and_audio_class(string name, uint classOfDevice, bool huaweiVid, bool expected) =>
        Assert.Equal(expected, WindowsHuaweiTransport.IsHuaweiEarbuds(name, classOfDevice, huaweiVid));

    [Fact]
    public void Finds_the_bluetooth_sig_huawei_vendor_id_in_the_address_instance_ids()
    {
        string[] ids =
        [
            @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2020\7&1&112233445566_C00000000",
            @"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&0001027D_PID&4113\B&23FB693A&0&C0DA5EA83EC7_C00000000",
        ];

        Assert.True(WindowsHuaweiTransport.HasHuaweiVendorId(ids, Address));
        Assert.False(WindowsHuaweiTransport.HasHuaweiVendorId(ids, 0x112233445566));
        Assert.False(WindowsHuaweiTransport.HasHuaweiVendorId(
            [@"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&0002027D_PID&4113\B&1&0&C0DA5EA83EC7_C00000000"], Address));
    }
}
