using System.IO;
using BatteryInfo.Core.History;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.App.Demo;

/// <summary>Writes seven days of plausible history for the demo devices, so the charts have something to show and
/// the store starts with last-known values (the asleep controller's 82 %, the disconnected Cetra's).</summary>
internal static class DemoHistory
{
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Range = TimeSpan.FromDays(7);

    /// <summary>Deletes the demo history folder so every demo run starts from the same story.</summary>
    public static void Reset(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    /// <param name="now">The current local time; devices sleep at night in local time.</param>
    public static void Write(HistoryStore store, DateTimeOffset now)
    {
        var start = now - Range;
        var lastNight = new DateTimeOffset(now.Date.AddDays(-1).AddHours(20), now.Offset);
        var points = new List<(DateTimeOffset Time, DeviceReading Device, BatteryComponent Component)>();

        void Add(DeviceReading device, string component, DateTimeOffset end, int endPercent, bool endCharging,
            double drainPerHour, double chargePerHour, int low, int fromHour, int toHour, int roundTo = 1)
        {
            foreach (var (time, percent, charge) in Simulate(start, end, endPercent, endCharging, drainPerHour, chargePerHour,
                         low, t => t.Hour >= fromHour && t.Hour < toHour))
            {
                var value = roundTo == 1 ? percent : (int)Math.Round(percent / (double)roundTo) * roundTo;
                points.Add((time, device, new BatteryComponent(component, value, charge)));
            }
        }

        Add(DemoDevices.Mouse, "", now, DemoBatteryProvider.LowMousePercent, false, 1.6, 30, 12, 8, 24);
        Add(DemoDevices.Keyboard, "", now, DemoBatteryProvider.KeyboardStartPercent, true, 2.5, 30, 15, 8, 24);
        Add(DemoDevices.FreeClip, "Left", now, 76, false, 9, 60, 25, 9, 23);
        Add(DemoDevices.FreeClip, "Right", now, 75, false, 9.5, 60, 25, 9, 23);
        Add(DemoDevices.FreeClip, "Case", now, 100, false, 1.2, 25, 45, 0, 24);
        Add(DemoDevices.FreeBuds, "", now, 80, false, 7, 50, 30, 10, 22, roundTo: 10);
        Add(DemoDevices.Controller, "", now - TimeSpan.FromMinutes(14), 82, false, 6, 30, 20, 17, 24);
        Add(DemoDevices.Cetra, "Left", lastNight, 64, false, 8, 50, 20, 9, 21);
        Add(DemoDevices.Cetra, "Right", lastNight, 66, false, 8, 50, 20, 9, 21);
        Add(DemoDevices.Cetra, "Case", lastNight, 80, false, 1, 25, 40, 0, 24);

        var snapshots = points
            .GroupBy(p => p.Time)
            .OrderBy(moment => moment.Key)
            .Select(moment => (IReadOnlyList<DeviceState>)moment
                .GroupBy(p => p.Device.DeviceId)
                .Select(device =>
                {
                    var reading = device.First().Device with
                    {
                        Status = DeviceStatus.Online,
                        Components = device.Select(p => p.Component).ToList(),
                        Timestamp = moment.Key,
                    };
                    return new DeviceState(reading, reading);
                })
                .ToList());
        store.AppendAll(snapshots);
    }

    // Walks back from the final value: discharging backwards means the level was higher, until it was full (it had
    // been charging before that); charging backwards means lower, until it was at the low mark.
    private static IEnumerable<(DateTimeOffset Time, int Percent, ChargeState Charge)> Simulate(DateTimeOffset start,
        DateTimeOffset end, int endPercent, bool endCharging, double drainPerHour, double chargePerHour, int low,
        Func<DateTimeOffset, bool> isAwake)
    {
        var points = new List<(DateTimeOffset, int, ChargeState)>();
        double level = endPercent;
        var charging = endCharging;
        var hours = Step.TotalHours;
        for (var time = end; time >= start; time -= Step)
        {
            var awake = isAwake(time) || time == end;
            if (awake) points.Add((time, (int)Math.Round(level), charging ? ChargeState.Charging : ChargeState.Discharging));
            if (charging)
            {
                level -= chargePerHour * hours;
                if (level <= low)
                {
                    level = low;
                    charging = false;
                }
            }
            else if (awake)
            {
                level += drainPerHour * hours;
                if (level >= 100)
                {
                    level = 100;
                    charging = true;
                }
            }
        }
        points.Reverse();
        return points;
    }
}
