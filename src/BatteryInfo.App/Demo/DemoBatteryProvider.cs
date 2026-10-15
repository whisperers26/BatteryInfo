using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;

namespace BatteryInfo.App.Demo;

/// <summary>Sample readings for --demo, covering every state: online and discharging, charging, low (raises the
/// toast), earbuds with left/right/case, an approximate Windows value, AirPods in 10 % steps, asleep (the history
/// holds its last value) and needs access. Touches no hardware.</summary>
internal sealed class DemoBatteryProvider(TimeProvider time) : IBatteryProvider
{
    public const int LowMousePercent = 18;
    public const int KeyboardStartPercent = 45;

    private readonly object _gate = new();
    private int _polls;

    public string Vendor => "Demo";

    public event EventHandler? RefreshRequested
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int poll;
        lock (_gate) poll = _polls++;
        var now = time.GetUtcNow();
        var keyboard = Math.Min(100, KeyboardStartPercent + poll);

        IReadOnlyList<DeviceReading> readings =
        [
            DemoDevices.Mouse with { Components = [BatteryComponent.Single(LowMousePercent, ChargeState.Discharging)], Timestamp = now },
            DemoDevices.Keyboard with
            {
                Components = [BatteryComponent.Single(keyboard, keyboard >= 100 ? ChargeState.Full : ChargeState.Charging)],
                Timestamp = now,
            },
            DemoDevices.FreeClip with
            {
                Components =
                [
                    new BatteryComponent("Left", 76, ChargeState.Discharging),
                    new BatteryComponent("Right", 75, ChargeState.Discharging),
                    new BatteryComponent("Case", 100, ChargeState.Discharging),
                ],
                Timestamp = now,
            },
            DemoDevices.FreeBuds with { Components = [BatteryComponent.Single(80, ChargeState.Unknown)], Timestamp = now },
            DemoDevices.AirPods with
            {
                Components =
                [
                    new BatteryComponent("Left", 80, ChargeState.Unknown),
                    new BatteryComponent("Right", 70, ChargeState.Unknown),
                    new BatteryComponent("Case", 60, ChargeState.Unknown),
                ],
                Timestamp = now,
            },
            DemoDevices.Controller with { Status = DeviceStatus.Asleep, Timestamp = now },
            DemoDevices.Razer with { Timestamp = now },
        ];
        return Task.FromResult(readings);
    }

    public void Dispose()
    {
    }
}
