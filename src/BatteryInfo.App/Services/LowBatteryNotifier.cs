using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.App.Services;

/// <summary>Runs the low-battery policy on every snapshot and shows one toast for whatever just became low. The
/// policy keeps running while notifications are off, so turning them back on does not replay old alerts.</summary>
internal sealed class LowBatteryNotifier(Action<string, string> showToast)
{
    public const string Title = "Low battery";

    private readonly LowBatteryPolicy _policy = new();

    public int Threshold
    {
        get => _policy.Threshold;
        set => _policy.Threshold = value;
    }

    public bool Enabled { get; set; } = true;

    public void Process(IReadOnlyList<DeviceState> snapshot)
    {
        var alerts = _policy.Evaluate(snapshot);
        if (alerts.Count == 0) return;

        var messages = alerts.Select(DeviceText.LowBatteryMessage).ToList();
        foreach (var message in messages) Log.Info($"{Title}: {message}{(Enabled ? "" : " (notifications are off)")}");
        if (Enabled) showToast(Title, string.Join('\n', messages));
    }
}
