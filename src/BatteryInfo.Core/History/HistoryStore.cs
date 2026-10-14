using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.Core.History;

/// <summary>Battery history as JSON lines in one file per UTC month (yyyy-MM.jsonl). A component's value is
/// written when it changes, and otherwise at most every <see cref="Heartbeat"/>. Thread-safe; never throws
/// for I/O problems or unreadable lines.</summary>
public sealed class HistoryStore(string directory, TimeProvider time)
{
    /// <summary>An unchanged value is written again after this long, so the chart has points to draw.</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(30);

    private const string MonthFormat = "yyyy-MM";
    private const string Extension = ".jsonl";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private Dictionary<string, HistoryRecord>? _lastWritten;

    public string Directory { get; } = directory;

    /// <summary>Records the fresh battery values in <paramref name="snapshot"/>. Stale, asleep and offline
    /// states carry no new value and are skipped.</summary>
    public void Append(IReadOnlyList<DeviceState> snapshot) => AppendAll([snapshot]);

    /// <summary>Appends several snapshots, oldest first, with one write per month file (for imports and the demo).</summary>
    public void AppendAll(IEnumerable<IReadOnlyList<DeviceState>> snapshots)
    {
        lock (_gate)
        {
            var lastWritten = _lastWritten ??= LoadLastWritten();
            var toWrite = new List<HistoryRecord>();
            foreach (var snapshot in snapshots) Collect(snapshot, lastWritten, toWrite);
            Write(toWrite);
        }
    }

    /// <summary>The records of one device from <paramref name="since"/> on, oldest first.</summary>
    public IReadOnlyList<HistoryRecord> Query(string deviceId, DateTimeOffset since)
    {
        lock (_gate)
        {
            return ReadRecords(since)
                .Where(r => r.Time >= since && string.Equals(r.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Time)
                .ToList();
        }
    }

    /// <summary>The newest value of every component of every device in the history, one Offline reading per
    /// device, to seed the device store at start-up.</summary>
    public IReadOnlyList<DeviceReading> LastKnownReadings()
    {
        lock (_gate)
        {
            return NewestPerComponent(ReadRecords(DateTimeOffset.MinValue))
                .GroupBy(r => r.DeviceId, StringComparer.OrdinalIgnoreCase)
                .Select(ToReading)
                .ToList();
        }
    }

    /// <summary>Deletes the month files whose whole month lies more than <paramref name="keep"/> in the past.</summary>
    public void Prune(TimeSpan keep)
    {
        var cutoff = time.GetUtcNow() - keep;
        lock (_gate)
        {
            foreach (var (path, month) in MonthFiles())
            {
                if (month.AddMonths(1) > cutoff) continue;
                try
                {
                    File.Delete(path);
                    Log.Info($"Deleted old history file {Path.GetFileName(path)}");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"Could not delete old history file {path}: {e.Message}");
                }
            }
        }
    }

    private static void Collect(IReadOnlyList<DeviceState> snapshot, Dictionary<string, HistoryRecord> lastWritten,
        List<HistoryRecord> toWrite)
    {
        foreach (var state in snapshot)
        {
            var reading = state.Latest;
            if (reading.Status == DeviceStatus.Offline || !reading.HasBattery) continue;

            var switchedSet = SwitchesSet(reading, lastWritten);
            foreach (var component in reading.Components)
            {
                if (component.Percent is null) continue;
                var record = new HistoryRecord(reading.Timestamp, reading.DeviceId, reading.Name, reading.Vendor,
                    reading.Kind, component.Name, component.Percent, component.Charge, reading.Status, reading.Approximate);
                var key = Key(record);
                if (lastWritten.TryGetValue(key, out var previous) && !ShouldWrite(previous, record, switchedSet)) continue;
                lastWritten[key] = record;
                toWrite.Add(record);
            }
        }
    }

    // A switch between a single value and named ones writes every value at once, so the set now read is always the
    // newest in the history (see NewestSet); unchanged values would otherwise wait for the heartbeat.
    private static bool ShouldWrite(HistoryRecord previous, HistoryRecord record, bool switchedSet) =>
        record.Time > previous.Time &&
        (switchedSet || record.Percent != previous.Percent || record.Charge != previous.Charge ||
         record.Status != previous.Status || record.Approximate != previous.Approximate ||
         record.Time - previous.Time >= Heartbeat);

    private static bool SwitchesSet(DeviceReading reading, Dictionary<string, HistoryRecord> lastWritten)
    {
        var newest = lastWritten.Values
            .Where(r => string.Equals(r.DeviceId, reading.DeviceId, StringComparison.OrdinalIgnoreCase))
            .MaxBy(r => r.Time);
        if (newest is null) return false;
        var readingIsSingle = reading.Components.Where(c => c.Percent is not null).All(c => IsSingle(c.Name));
        return IsSingle(newest.Component) != readingIsSingle;
    }

    private static string Key(HistoryRecord record) => $"{record.DeviceId}\n{record.Component}";

    private Dictionary<string, HistoryRecord> LoadLastWritten() =>
        NewestPerComponent(ReadRecords(DateTimeOffset.MinValue)).ToDictionary(Key, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<HistoryRecord> NewestPerComponent(IEnumerable<HistoryRecord> records) =>
        records
            .GroupBy(Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MaxBy(r => r.Time)!);

    private static DeviceReading ToReading(IEnumerable<HistoryRecord> componentRecords)
    {
        var records = NewestSet(componentRecords.ToList())
            .OrderBy(r => ComponentOrder.Rank(r.Component)).ThenBy(r => r.Component).ToList();
        var newest = records.MaxBy(r => r.Time)!;
        return new DeviceReading(newest.DeviceId, newest.Vendor, newest.DeviceName, newest.Kind, DeviceStatus.Offline,
            records.Select(r => new BatteryComponent(r.Component, r.Percent, r.Charge)).ToList(), newest.Time, "History",
            newest.Approximate);
    }

    // A device can have both a single value and named ones in its history: HUAWEI earbuds are read per bud over SPP,
    // and fall back to Windows' one value while another app holds SPP. Only the set written last describes the
    // device; named values win a tie.
    private static IReadOnlyList<HistoryRecord> NewestSet(IReadOnlyList<HistoryRecord> componentRecords)
    {
        var single = componentRecords.Where(r => IsSingle(r.Component)).ToList();
        var named = componentRecords.Where(r => !IsSingle(r.Component)).ToList();
        if (single.Count == 0 || named.Count == 0) return componentRecords;
        return single.Max(r => r.Time) > named.Max(r => r.Time) ? single : named;
    }

    private static bool IsSingle(string component) => component.Length == 0;

    private void Write(List<HistoryRecord> records)
    {
        if (records.Count == 0) return;
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var month in records.GroupBy(r => r.Time.UtcDateTime.ToString(MonthFormat, CultureInfo.InvariantCulture)))
            {
                AppendLines(Path.Combine(Directory, month.Key + Extension), month.Select(r => JsonSerializer.Serialize(r, Json)));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not write battery history to {Directory}: {e.Message}");
        }
    }

    // Starts on a fresh line even when a crash left the last line unfinished, so only that line is lost.
    private static void AppendLines(string path, IEnumerable<string> lines)
    {
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var text = new StringBuilder();
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            if (stream.ReadByte() != '\n') text.Append('\n');
        }
        foreach (var line in lines) text.Append(line).Append('\n');
        stream.Seek(0, SeekOrigin.End);
        stream.Write(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private IEnumerable<HistoryRecord> ReadRecords(DateTimeOffset since)
    {
        var records = new List<HistoryRecord>();
        foreach (var (path, month) in MonthFiles().OrderBy(f => f.Month))
        {
            if (month.AddMonths(1) <= since) continue;
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (Parse(line) is { } record) records.Add(record);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not read battery history file {path}: {e.Message}");
            }
        }
        return records;
    }

    private static HistoryRecord? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        HistoryRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<HistoryRecord>(line, Json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (record?.DeviceId is not { Length: > 0 }) return null;
        return record with
        {
            DeviceName = record.DeviceName ?? record.DeviceId,
            Vendor = record.Vendor ?? "",
            Component = record.Component ?? "",
        };
    }

    private List<(string Path, DateTimeOffset Month)> MonthFiles()
    {
        var files = new List<(string, DateTimeOffset)>();
        if (!System.IO.Directory.Exists(Directory)) return files;
        try
        {
            foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension))
            {
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), MonthFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var month))
                {
                    files.Add((path, new DateTimeOffset(month, TimeSpan.Zero)));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not list battery history files in {Directory}: {e.Message}");
        }
        return files;
    }
}
