using BatteryInfo.Core.History;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Tests.Fakes;

namespace BatteryInfo.Core.Tests.History;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BatteryInfoTests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private HistoryStore NewStore() => new(_directory, _time);

    private DeviceState Mouse(int? percent, ChargeState charge = ChargeState.Discharging,
        DeviceStatus status = DeviceStatus.Online, DateTimeOffset? at = null, bool approximate = false)
    {
        var reading = new DeviceReading("logitech:B4655832", "Logitech", "PRO X2 SUPERSTRIKE", DeviceKind.Mouse, status,
            percent is null ? [] : [BatteryComponent.Single(percent, charge)], at ?? _time.GetUtcNow(), "HID++ 0x1004",
            approximate);
        return new DeviceState(reading, reading.HasBattery ? reading : null);
    }

    private DeviceState Earbuds(int left, int right, int caseLevel, DateTimeOffset? at = null)
    {
        var reading = new DeviceReading("huawei:C0DA5EA83EC7", "HUAWEI", "HUAWEI FreeClip 2", DeviceKind.Earbuds,
            DeviceStatus.Online,
            [
                new BatteryComponent("Left", left, ChargeState.Discharging),
                new BatteryComponent("Right", right, ChargeState.Discharging),
                new BatteryComponent("Case", caseLevel, ChargeState.Charging),
            ],
            at ?? _time.GetUtcNow(), "SPP");
        return new DeviceState(reading, reading);
    }

    private IReadOnlyList<HistoryRecord> AllMouse(HistoryStore store) =>
        store.Query("logitech:B4655832", DateTimeOffset.MinValue);

    [Fact]
    public void Append_then_Query_round_trips_every_field()
    {
        var store = NewStore();
        var at = _time.GetUtcNow();

        store.Append([Mouse(56)]);

        var record = Assert.Single(AllMouse(store));
        Assert.Equal(new HistoryRecord(at, "logitech:B4655832", "PRO X2 SUPERSTRIKE", "Logitech", DeviceKind.Mouse, "", 56,
            ChargeState.Discharging, DeviceStatus.Online), record);
    }

    [Fact]
    public void Append_writes_one_line_per_component_into_a_monthly_jsonl_file()
    {
        var store = NewStore();

        store.Append([Earbuds(76, 75, 100)]);

        var file = Path.Combine(_directory, "2026-10.jsonl");
        Assert.Equal(3, File.ReadAllLines(file).Length);
        var records = store.Query("huawei:C0DA5EA83EC7", DateTimeOffset.MinValue);
        Assert.Equal(["Left", "Right", "Case"], records.Select(r => r.Component));
        Assert.Equal([76, 75, 100], records.Select(r => r.Percent!.Value));
        Assert.Equal(ChargeState.Charging, records[2].Charge);
    }

    [Fact]
    public void Unchanged_values_are_written_once_per_30_minutes()
    {
        var store = NewStore();

        for (var minute = 0; minute <= 58; minute += 2)
        {
            store.Append([Mouse(56)]);
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        var records = AllMouse(store);
        Assert.Equal(2, records.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), records[1].Time - records[0].Time);
    }

    [Fact]
    public void AppendAll_writes_what_one_Append_per_snapshot_would()
    {
        var store = NewStore();
        var t0 = _time.GetUtcNow();

        store.AppendAll(
        [
            [Mouse(56, at: t0)],
            [Mouse(56, at: t0.AddMinutes(10)), Earbuds(76, 75, 100, at: t0.AddMinutes(10))],
            [Mouse(56, at: t0.AddMinutes(30))],
            [Mouse(55, at: t0.AddMinutes(32))],
        ]);

        Assert.Equal([56, 56, 55], AllMouse(store).Select(r => r.Percent!.Value));
        Assert.Equal(3, store.Query("huawei:C0DA5EA83EC7", DateTimeOffset.MinValue).Count);
        Assert.Equal(6, File.ReadAllLines(Path.Combine(_directory, "2026-10.jsonl")).Length);
    }

    [Fact]
    public void A_changed_percent_or_charge_state_is_written_at_once()
    {
        var store = NewStore();

        store.Append([Mouse(56)]);
        _time.Advance(TimeSpan.FromMinutes(2));
        store.Append([Mouse(55)]);
        _time.Advance(TimeSpan.FromMinutes(2));
        store.Append([Mouse(55, ChargeState.Charging)]);

        Assert.Equal([56, 55, 55], AllMouse(store).Select(r => r.Percent!.Value));
        Assert.Equal(ChargeState.Charging, AllMouse(store)[2].Charge);
    }

    [Fact]
    public void An_approximate_value_is_recorded_as_approximate()
    {
        NewStore().Append([Mouse(56, approximate: true)]);

        Assert.True(Assert.Single(AllMouse(NewStore())).Approximate);
    }

    [Fact]
    public void A_switch_between_an_exact_and_an_approximate_value_is_written_at_once()
    {
        var store = NewStore();

        store.Append([Mouse(56)]);
        _time.Advance(TimeSpan.FromMinutes(2));
        store.Append([Mouse(56, approximate: true)]);

        Assert.Equal([false, true], AllMouse(store).Select(r => r.Approximate));
    }

    [Fact]
    public void Lines_written_without_the_approximate_flag_read_as_exact()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "2026-10.jsonl"),
            "{\"time\":\"2026-10-14T12:00:00+00:00\",\"deviceId\":\"logitech:B4655832\",\"deviceName\":\"PRO X2 SUPERSTRIKE\"," +
            "\"vendor\":\"Logitech\",\"kind\":\"Mouse\",\"component\":\"\",\"percent\":56,\"charge\":\"Discharging\",\"status\":\"Online\"}\n");

        var record = Assert.Single(AllMouse(NewStore()));

        Assert.Equal(56, record.Percent);
        Assert.False(record.Approximate);
    }

    [Fact]
    public void The_same_reading_appended_again_is_not_written_twice()
    {
        var store = NewStore();
        var state = Mouse(56);

        store.Append([state]);
        _time.Advance(TimeSpan.FromHours(1));
        store.Append([state]);

        Assert.Single(AllMouse(store));
    }

    [Fact]
    public void States_without_a_fresh_battery_value_are_not_written()
    {
        var store = NewStore();
        var stale = Mouse(56);
        var asleep = stale with { Latest = stale.Latest with { Status = DeviceStatus.Asleep, Components = [], Timestamp = _time.GetUtcNow().AddMinutes(5) } };
        var offline = stale with { Latest = stale.Latest with { Status = DeviceStatus.Offline, Timestamp = _time.GetUtcNow().AddMinutes(40) } };

        store.Append([asleep]);
        store.Append([offline]);

        Assert.Empty(AllMouse(store));
    }

    [Fact]
    public void A_new_store_on_the_same_directory_remembers_what_was_written()
    {
        NewStore().Append([Mouse(56)]);
        _time.Advance(TimeSpan.FromMinutes(5));

        var restarted = NewStore();
        restarted.Append([Mouse(56)]);

        Assert.Single(AllMouse(restarted));
    }

    [Fact]
    public void Query_filters_by_device_and_time_across_month_files()
    {
        var store = NewStore();
        var september = new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero);
        store.Append([Mouse(60, at: september)]);
        store.Append([Mouse(59, at: september.AddHours(2))]);
        store.Append([Earbuds(76, 75, 100, at: september.AddHours(2))]);
        store.Append([Mouse(58, at: september.AddHours(4))]);

        var records = store.Query("logitech:B4655832", september.AddHours(1));

        Assert.True(File.Exists(Path.Combine(_directory, "2026-09.jsonl")));
        Assert.True(File.Exists(Path.Combine(_directory, "2026-10.jsonl")));
        Assert.Equal([59, 58], records.Select(r => r.Percent!.Value));
    }

    [Fact]
    public void Query_skips_corrupt_and_truncated_lines()
    {
        var store = NewStore();
        store.Append([Mouse(56)]);
        _time.Advance(TimeSpan.FromMinutes(2));
        var file = Path.Combine(_directory, "2026-10.jsonl");
        File.AppendAllText(file, "not json at all\n{}\n{\"time\":\"2026-10-14T12:01:00+00:00\",\"deviceId\":\"logitech:B465\n");
        store.Append([Mouse(55)]);

        Assert.Equal([56, 55], NewStore().Query("logitech:B4655832", DateTimeOffset.MinValue).Select(r => r.Percent!.Value));
    }

    [Fact]
    public void Append_after_a_line_cut_off_by_a_crash_starts_a_fresh_line()
    {
        var store = NewStore();
        store.Append([Mouse(56)]);
        _time.Advance(TimeSpan.FromMinutes(2));
        var file = Path.Combine(_directory, "2026-10.jsonl");
        File.AppendAllText(file, "{\"time\":\"2026-10-14T12:01:00+00:00\",\"deviceId\":\"logitech:B465"); // no newline

        store.Append([Mouse(55)]);

        Assert.Equal([56, 55], NewStore().Query("logitech:B4655832", DateTimeOffset.MinValue).Select(r => r.Percent!.Value));
    }

    [Fact]
    public void LastKnownReadings_skips_lines_without_a_device_id()
    {
        var store = NewStore();
        store.Append([Mouse(56)]);
        var file = Path.Combine(_directory, "2026-10.jsonl");
        File.AppendAllText(file, "{}\nnull\n{\"deviceId\":\"\",\"percent\":40}\nnot json at all\n{\"time\":\"2026-10-14T12:01:00+00:00\",\"deviceId\":\"logitech:B465");

        var readings = NewStore().LastKnownReadings();

        var reading = Assert.Single(readings);
        Assert.Equal("logitech:B4655832", reading.DeviceId);
        Assert.Equal(56, Assert.Single(reading.Components).Percent);
    }

    [Fact]
    public void Query_returns_nothing_when_the_directory_does_not_exist()
    {
        Assert.Empty(NewStore().Query("logitech:B4655832", DateTimeOffset.MinValue));
        Assert.Empty(NewStore().LastKnownReadings());
    }

    [Fact]
    public void LastKnownReadings_returns_the_newest_values_of_each_device()
    {
        var store = NewStore();
        store.Append([Mouse(60), Earbuds(80, 79, 100)]);
        _time.Advance(TimeSpan.FromMinutes(10));
        store.Append([Mouse(59), Earbuds(78, 79, 100)]);
        var newest = _time.GetUtcNow();

        var readings = NewStore().LastKnownReadings().OrderBy(r => r.DeviceId).ToList();

        Assert.Equal(2, readings.Count);
        var earbuds = readings[0];
        Assert.Equal("huawei:C0DA5EA83EC7", earbuds.DeviceId);
        Assert.Equal("HUAWEI", earbuds.Vendor);
        Assert.Equal("HUAWEI FreeClip 2", earbuds.Name);
        Assert.Equal(DeviceKind.Earbuds, earbuds.Kind);
        Assert.Equal(DeviceStatus.Offline, earbuds.Status);
        Assert.Equal(newest, earbuds.Timestamp);
        Assert.Equal(["Left", "Right", "Case"], earbuds.Components.Select(c => c.Name));
        Assert.Equal([78, 79, 100], earbuds.Components.Select(c => c.Percent!.Value));
        var mouse = readings[1];
        Assert.Equal(BatteryComponent.Single(59, ChargeState.Discharging), Assert.Single(mouse.Components));
        Assert.Equal(newest, mouse.Timestamp);
    }

    [Fact]
    public void Prune_deletes_month_files_that_ended_before_the_cutoff()
    {
        Directory.CreateDirectory(_directory);
        foreach (var name in new[] { "2026-05.jsonl", "2026-06.jsonl", "2026-07.jsonl", "2026-10.jsonl", "notes.txt" })
        {
            File.WriteAllText(Path.Combine(_directory, name), "");
        }

        // Now is 2026-10-14, so 90 days back is 2026-07-16: June ended before it, July did not.
        NewStore().Prune(TimeSpan.FromDays(90));

        Assert.Equal(["2026-07.jsonl", "2026-10.jsonl", "notes.txt"],
            Directory.GetFiles(_directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }
}
