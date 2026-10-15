using System.Runtime.InteropServices.WindowsRuntime;
using BatteryInfo.Core.Bluetooth;
using BatteryInfo.Core.Safety;
using Windows.Devices.Bluetooth.Advertisement;

namespace BatteryInfo.Core.Tests.Bluetooth;

public class PassiveAdvertisementListenerTests
{
    [Fact]
    public void New_watchers_scan_passively()
    {
        var watcher = PassiveAdvertisementListener.CreateWatcher();

        Assert.Equal(BluetoothLEScanningMode.Passive, watcher.ScanningMode);
        Assert.Equal(BluetoothLEAdvertisementWatcherStatus.Created, watcher.Status);
    }

    [Fact]
    public void An_active_watcher_is_refused_before_it_starts()
    {
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };

        Assert.Throws<UnsafeCommandException>(() => PassiveAdvertisementListener.EnsurePassive(watcher));
        Assert.Equal(BluetoothLEAdvertisementWatcherStatus.Created, watcher.Status);
    }

    [Fact]
    public void Sections_are_every_entry_of_the_company_without_its_id()
    {
        var advertisement = new BluetoothLEAdvertisement();
        advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData(0x004C, new byte[] { 0x10, 0x05, 0x01 }.AsBuffer()));
        advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData(0x0006, new byte[] { 0x01, 0x09 }.AsBuffer()));
        advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData(0x004C, new byte[] { 0x07, 0x19, 0x01 }.AsBuffer()));

        var sections = PassiveAdvertisementListener.Sections(advertisement, 0x004C);

        Assert.Equal([[0x10, 0x05, 0x01], [0x07, 0x19, 0x01]], sections);
    }

    [Fact]
    public void An_advertisement_without_the_company_has_no_sections()
    {
        var advertisement = new BluetoothLEAdvertisement();
        advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData(0x0075, new byte[] { 0x42 }.AsBuffer()));

        Assert.Empty(PassiveAdvertisementListener.Sections(advertisement, 0x004C));
    }
}
