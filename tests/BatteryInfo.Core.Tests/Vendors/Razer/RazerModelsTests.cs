using BatteryInfo.Core.Model;
using BatteryInfo.Core.Vendors.Razer;

namespace BatteryInfo.Core.Tests.Vendors.Razer;

public class RazerModelsTests
{
    [Theory]
    [InlineData(0x00D6, RazerLink.Wired)]
    [InlineData(0x00D7, RazerLink.Dongle)]
    public void Basilisk_v3_pro_35k_is_found_by_its_usb_pids(ushort pid, RazerLink link)
    {
        var match = RazerModels.FindUsb(0x1532, pid);

        Assert.NotNull(match);
        Assert.Equal(link, match.Value.Link);
        Assert.Equal("Razer Basilisk V3 Pro 35K", match.Value.Model.Name);
        Assert.Equal(DeviceKind.Mouse, match.Value.Model.Kind);
    }

    [Fact]
    public void Cable_and_dongle_share_one_device_id_named_after_the_wired_pid()
    {
        Assert.Equal("razer:00D6", RazerModels.FindUsb(0x1532, 0x00D6)!.Value.Model.DeviceId);
        Assert.Equal("razer:00D6", RazerModels.FindUsb(0x1532, 0x00D7)!.Value.Model.DeviceId);
    }

    [Fact]
    public void Model_records_its_bluetooth_pid_and_transaction_id()
    {
        var model = RazerModels.FindUsb(0x1532, 0x00D6)!.Value.Model;

        Assert.Equal((ushort)0x00D8, model.BluetoothPid);
        Assert.Equal(0x1F, model.TransactionId);
    }

    [Theory]
    [InlineData(0x1532, 0x00D8)] // Bluetooth PID: not a USB link
    [InlineData(0x1532, 0x0099)] // another Razer product
    [InlineData(0x068E, 0x00D6)] // Razer's Bluetooth vendor id
    [InlineData(0x046D, 0x00D7)] // another vendor
    public void Other_ids_are_not_matched(ushort vid, ushort pid)
    {
        Assert.Null(RazerModels.FindUsb(vid, pid));
        Assert.False(RazerModels.IsKnownUsb(vid, pid));
    }

    [Fact]
    public void Is_known_usb_accepts_both_links()
    {
        Assert.True(RazerModels.IsKnownUsb(0x1532, 0x00D6));
        Assert.True(RazerModels.IsKnownUsb(0x1532, 0x00D7));
    }
}
