using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Tests.Fakes;

namespace BatteryInfo.Core.Tests.Monitoring;

public class DeviceStoreTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly DeviceStore _store;

    public DeviceStoreTests() => _store = new DeviceStore(_time, TimeSpan.FromDays(7));

    private DeviceReading Reading(string id, string vendor, int? percent, DeviceStatus status = DeviceStatus.Online) =>
        new(id, vendor, id, DeviceKind.Mouse, status,
            percent is null ? [] : [BatteryComponent.Single(percent, ChargeState.Discharging)],
            _time.GetUtcNow(), "test");

    [Fact]
    public void Apply_stores_latest_reading()
    {
        _store.Apply("Logitech", [Reading("logitech:1", "Logitech", 56)]);

        var state = Assert.Single(_store.Snapshot());
        Assert.Equal(56, state.DisplayComponents[0].Percent);
        Assert.False(state.IsStale);
        Assert.Equal(DeviceStatus.Online, state.Latest.Status);
    }

    [Fact]
    public void Reading_without_battery_keeps_last_known_value_as_stale()
    {
        _store.Apply("Razer", [Reading("razer:00D6", "Razer", 82)]);
        var readAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromMinutes(10));

        _store.Apply("Razer", [Reading("razer:00D6", "Razer", null, DeviceStatus.Asleep)]);

        var state = Assert.Single(_store.Snapshot());
        Assert.Equal(DeviceStatus.Asleep, state.Latest.Status);
        Assert.True(state.IsStale);
        Assert.Equal(82, state.DisplayComponents[0].Percent);
        Assert.Equal(readAt, state.BatteryTimestamp);
    }

    [Fact]
    public void Device_missing_from_its_vendor_poll_becomes_offline()
    {
        _store.Apply("Betop", [Reading("betop:KP70C2", "Betop", 91)]);
        var readAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromMinutes(2));

        _store.Apply("Betop", []);

        var state = Assert.Single(_store.Snapshot());
        Assert.Equal(DeviceStatus.Offline, state.Latest.Status);
        Assert.Equal(readAt, state.Latest.Timestamp);
        Assert.Null(state.Latest.Detail);
        Assert.Equal(91, state.DisplayComponents[0].Percent);
    }

    [Fact]
    public void Apply_leaves_other_vendors_untouched()
    {
        _store.Apply("Betop", [Reading("betop:KP70C2", "Betop", 91)]);

        _store.Apply("ROG", [Reading("rog:azoth-x:T4", "ROG", 45)]);

        Assert.Equal(2, _store.Snapshot().Count);
        Assert.All(_store.Snapshot(), s => Assert.Equal(DeviceStatus.Online, s.Latest.Status));
    }

    [Fact]
    public void Snapshot_drops_devices_not_seen_within_retention()
    {
        _store.Apply("Betop", [Reading("betop:KP70C2", "Betop", 91)]);
        _store.Apply("Betop", []);

        _time.Advance(TimeSpan.FromDays(8));

        Assert.Empty(_store.Snapshot());
    }

    [Fact]
    public void Seeded_device_is_offline_until_polled()
    {
        _store.Seed(Reading("huawei:C0DA5EA83EC7", "HUAWEI", 77));

        var state = Assert.Single(_store.Snapshot());
        Assert.Equal(DeviceStatus.Offline, state.Latest.Status);
        Assert.Equal(77, state.DisplayComponents[0].Percent);
    }

    [Fact]
    public void MarkVendorFailed_keeps_states_and_sets_detail()
    {
        _store.Apply("ROG", [Reading("rog:azoth-x:T4", "ROG", 45)]);

        _store.MarkVendorFailed("ROG", "provider crashed");

        var state = Assert.Single(_store.Snapshot());
        Assert.Equal(45, state.DisplayComponents[0].Percent);
        Assert.Equal("provider crashed", state.Latest.Detail);
    }

    [Fact]
    public void Snapshot_is_ordered_by_vendor_then_name()
    {
        _store.Apply("ROG", [Reading("rog:b", "ROG", 1)]);
        _store.Apply("Logitech", [Reading("logitech:z", "Logitech", 2), Reading("logitech:a", "Logitech", 3)]);

        Assert.Equal(["logitech:a", "logitech:z", "rog:b"], _store.Snapshot().Select(s => s.Latest.DeviceId));
    }
}
