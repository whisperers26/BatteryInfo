using BatteryInfo.Core.Model;
using BatteryInfo.Core.Vendors.Rog;

namespace BatteryInfo.Core.Tests.Vendors.Rog;

public class RogModelsTests
{
    [Theory]
    [InlineData(0x1C24, RogLink.Usb)]
    [InlineData(0x1C25, RogLink.Wireless)]
    [InlineData(0x1C26, RogLink.Wireless)]
    [InlineData(0x1C27, RogLink.Bluetooth)]
    public void Azoth_x_pids_are_one_keyboard_model(int pid, RogLink link)
    {
        var model = RogModels.Find((ushort)pid);

        Assert.NotNull(model);
        Assert.Equal("azoth-x", model.Slug);
        Assert.Equal("ROG Azoth X", model.Name);
        Assert.Equal(DeviceKind.Keyboard, model.Kind);
        Assert.Equal(link, model.Link);
    }

    [Theory]
    [InlineData(0x1ACE, "omni-receiver")]
    [InlineData(0x1ACF, "omni-receiver")]
    [InlineData(0x1AD0, "speednova-8k-receiver")]
    [InlineData(0x1AD1, "speednova-8k-receiver")]
    [InlineData(0x1D54, "asus-dongle")]
    public void Omni_class_receivers(int pid, string slug)
    {
        var model = RogModels.Find((ushort)pid);

        Assert.NotNull(model);
        Assert.Equal(RogLink.Receiver, model.Link);
        Assert.Equal(slug, model.Slug);
    }

    [Fact]
    public void Omni_receiver_name()
    {
        Assert.Equal("ROG OMNI Receiver", RogModels.Find(0x1ACE)!.Name);
    }

    [Fact]
    public void Cetra_is_wireless_earbuds()
    {
        var model = RogModels.Find(0x1CAC);

        Assert.NotNull(model);
        Assert.Equal(("cetra-open-wireless", "ROG Cetra Open Wireless", DeviceKind.Earbuds, RogLink.Wireless),
            (model.Slug, model.Name, model.Kind, model.Link));
    }

    [Theory]
    [InlineData(0x1960, "ROG Keris Wireless")]
    [InlineData(0x197F, "ROG Gladius III Wireless")]
    public void Older_own_dongle_mice(int pid, string name)
    {
        var model = RogModels.Find((ushort)pid);

        Assert.NotNull(model);
        Assert.Equal((name, DeviceKind.Mouse, RogLink.Wireless), (model.Name, model.Kind, model.Link));
    }

    [Fact]
    public void Other_asus_devices_are_unknown()
    {
        Assert.Null(RogModels.Find(0x1BCA)); // ROG Gaming Display Aura device on this PC: never touched.
        Assert.False(RogModels.IsSupported(0x0B05, 0x1BCA));
    }

    [Fact]
    public void Only_asus_vendor_id_is_supported()
    {
        Assert.True(RogModels.IsSupported(0x0B05, 0x1ACE));
        Assert.False(RogModels.IsSupported(0x046D, 0x1ACE));
    }

    [Fact]
    public void Slot_with_a_known_pid_uses_the_table()
    {
        Assert.Equal(RogModels.Find(0x1C25), RogModels.ForSlot(new RogSlot(0x1C25, 2, 4)));
    }

    [Theory]
    [InlineData(2, DeviceKind.Keyboard, "ROG keyboard")]
    [InlineData(3, DeviceKind.Mouse, "ROG mouse")]
    [InlineData(7, DeviceKind.Other, "ROG device")]
    public void Slot_with_an_unknown_pid_takes_its_kind_from_the_control_report_id(int controlReportId, DeviceKind kind, string name)
    {
        var model = RogModels.ForSlot(new RogSlot(0x1A2B, (byte)controlReportId, 5));

        Assert.Equal((ushort)0x1A2B, model.ProductId);
        Assert.Equal("1a2b", model.Slug);
        Assert.Equal(name, model.Name);
        Assert.Equal(kind, model.Kind);
        Assert.Equal(RogLink.Wireless, model.Link);
    }
}
