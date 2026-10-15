using BatteryInfo.Core.Vendors.Apple;

namespace BatteryInfo.Core.Tests.Vendors.Apple;

public class WindowsAppleBluetoothTests
{
    private const ulong Address = 0x282D7FB83C8B;

    // From a Windows device dump of AirPods Pro 2 (USB-C) in ntpods issue #11: the device node has no Device ID record,
    // its service nodes do.
    private static readonly string[] AirPodsPro2Usbc =
    [
        @"BTHENUM\DEV_282D7FB83C8B\7&3549F023&2&BLUETOOTHDEVICE_282D7FB83C8B",
        @"BTHENUM\{00001000-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2024\7&3549F023&2&282D7FB83C8B_C00000000",
        @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2024\7&3549F023&2&282D7FB83C8B_C00000000",
    ];

    [Fact]
    public void Reads_the_pid_of_apples_device_id_record_on_a_service_node() =>
        Assert.Equal((ushort)0x2024, WindowsAppleBluetooth.AppleModelId(AirPodsPro2Usbc, Address));

    [Fact]
    public void Reads_the_owners_airpods_4_anc_as_windows_lists_them()
    {
        // This PC on 2026-10-15, "David's AirPods - Find My" (paired, not connected): Get-PnpDevice.
        string[] ids =
        [
            @"BTHENUM\{00001801-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&201B\8&5DD1202&0&9CA9C520AFB8_C00000000",
            @"BTHENUM\{74EC2172-0BAD-4D01-8F77-997B2BE0722A}_VID&0001004C_PID&201B\8&5DD1202&0&9CA9C520AFB8_C00000000",
            @"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&201B\8&5DD1202&0&9CA9C520AFB8_C00000000",
        ];

        Assert.Equal((ushort)0x201B, WindowsAppleBluetooth.AppleModelId(ids, 0x9CA9C520AFB8));
    }

    [Fact]
    public void Matches_ids_in_any_case() =>
        Assert.Equal((ushort)0x2014, WindowsAppleBluetooth.AppleModelId(
            [@"bthenum\{0000110b-0000-1000-8000-00805f9b34fb}_vid&0001004c_pid&2014\7&1&282d7fb83c8b_c00000000"], Address));

    [Fact]
    public void Ignores_nodes_of_other_addresses() =>
        Assert.Null(WindowsAppleBluetooth.AppleModelId(AirPodsPro2Usbc, 0x112233445566));

    [Theory]
    [InlineData(@"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&0001027D_PID&4113\B&23FB693A&0&282D7FB83C8B_C00000000")] // HUAWEI
    [InlineData(@"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0002004C_PID&2024\7&1&282D7FB83C8B_C00000000")] // USB vendor ids
    [InlineData(@"BTHENUM\DEV_282D7FB83C8B\7&3549F023&2&BLUETOOTHDEVICE_282D7FB83C8B")]
    [InlineData(@"BTHENUM\282D7FB83C8B_VID&0001004C_PID&20")]
    [InlineData(@"BTHENUM\282D7FB83C8B_VID&0001004C_PID&ZZZZ\1")]
    public void Other_ids_carry_no_apple_model(string id) =>
        Assert.Null(WindowsAppleBluetooth.AppleModelId([id], Address));
}
