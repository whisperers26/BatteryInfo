using BatteryInfo.Core.Hid;

namespace BatteryInfo.Core.Tests.Hid;

public class HidDeviceInfoTests
{
    [Theory]
    [InlineData(@"\\?\hid#vid_046d&pid_c54d&mi_02&col01#c&1823a09c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 2)]
    [InlineData(@"\\?\HID#VID_0B05&PID_1ACE&MI_03&COL05#c&26064ec0&0&0004#{4d1e55b2-f16f-11cf-88cb-001111000030}", 3)]
    [InlineData(@"\\?\hid#vid_20bc&pid_518d&ig_00#c&2e390e2&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", null)]
    public void ParseInterfaceNumber_reads_mi_segment(string path, int? expected)
    {
        Assert.Equal(expected, HidDeviceInfo.ParseInterfaceNumber(path));
    }

    [Theory]
    [InlineData(@"\\?\hid#vid_046d&pid_c54d&mi_02&col01#c&1823a09c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 0x046D, 0xC54D)]
    [InlineData(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002046d_pid&b023&col01#9&1&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 0x046D, 0xB023)]
    [InlineData(@"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02068e_pid&00d8_rev&0100#a&1&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 0x068E, 0x00D8)]
    public void ParseVendorProduct_handles_usb_bluetooth_and_ble_paths(string path, int vid, int pid)
    {
        var parsed = HidDeviceInfo.ParseVendorProduct(path);

        Assert.Equal(((ushort)vid, (ushort)pid), parsed);
    }

    [Fact]
    public void ParseVendorProduct_returns_null_without_ids()
    {
        Assert.Null(HidDeviceInfo.ParseVendorProduct(@"\\?\hid#something#1"));
    }
}
