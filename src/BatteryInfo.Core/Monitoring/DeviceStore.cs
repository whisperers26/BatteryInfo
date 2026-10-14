using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Monitoring;

/// <summary>Merges provider poll results into per-device state. Thread-safe.</summary>
public sealed class DeviceStore(TimeProvider time, TimeSpan retention)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceState> _states = new(StringComparer.OrdinalIgnoreCase);

    public DeviceStore(TimeProvider time) : this(time, TimeSpan.FromDays(7))
    {
    }

    /// <summary>Adds a device remembered from history. It shows as Offline until a provider reports it.</summary>
    public void Seed(DeviceReading reading)
    {
        lock (_gate)
        {
            if (_states.ContainsKey(reading.DeviceId)) return;
            var offline = reading with { Status = DeviceStatus.Offline, Detail = null };
            _states[reading.DeviceId] = new DeviceState(offline, offline.HasBattery ? offline : null);
        }
    }

    /// <summary>Applies one complete poll of <paramref name="vendor"/>. That vendor's devices missing from it go Offline,
    /// or are dropped when they never had a battery value: such a row (a receiver that failed, say) has nothing to show.</summary>
    public void Apply(string vendor, IReadOnlyList<DeviceReading> readings)
    {
        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reading in readings)
            {
                seen.Add(reading.DeviceId);
                _states.TryGetValue(reading.DeviceId, out var previous);
                var lastWithBattery = reading.HasBattery ? reading : previous?.LastWithBattery;
                _states[reading.DeviceId] = new DeviceState(reading, lastWithBattery);
            }

            foreach (var (id, state) in _states.ToList())
            {
                if (seen.Contains(id) || !IsVendor(state, vendor) || state.Latest.Status == DeviceStatus.Offline) continue;
                if (state.LastWithBattery is null) _states.Remove(id);
                else _states[id] = state with { Latest = state.Latest with { Status = DeviceStatus.Offline, Detail = null } };
            }
        }
    }

    /// <summary>The vendor's provider failed: keep its devices as they were and note why.</summary>
    public void MarkVendorFailed(string vendor, string detail)
    {
        lock (_gate)
        {
            foreach (var (id, state) in _states.ToList())
            {
                if (IsVendor(state, vendor)) _states[id] = state with { Latest = state.Latest with { Detail = detail } };
            }
        }
    }

    public IReadOnlyList<DeviceState> Snapshot()
    {
        lock (_gate)
        {
            var cutoff = time.GetUtcNow() - retention;
            foreach (var (id, state) in _states.ToList())
            {
                var lastSeen = state.Latest.Timestamp > (state.BatteryTimestamp ?? DateTimeOffset.MinValue)
                    ? state.Latest.Timestamp
                    : state.BatteryTimestamp!.Value;
                if (lastSeen < cutoff) _states.Remove(id);
            }

            return _states.Values
                .OrderBy(s => s.Latest.Vendor, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Latest.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static bool IsVendor(DeviceState state, string vendor) =>
        string.Equals(state.Latest.Vendor, vendor, StringComparison.OrdinalIgnoreCase);
}
