using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.App.Services;

/// <summary>Runs the low-battery policy on every snapshot and shows one toast for whatever just became low. The
/// policy keeps running while notifications are off, so turning them back on does not replay old alerts.</summary>
internal sealed class LowBatteryNotifier
{
    public const string Title = "Low battery";

    private readonly LowBatteryPolicy _policy = new();
    private readonly Action<string, string> _showToast;
    private readonly LowBatteryAlertStore? _alertStore;
    private IReadOnlySet<string> _saved;

    /// <param name="alertStore">Keeps the alerted components across restarts, so a restart does not repeat a toast
    /// already shown. Without one they are kept in memory only.</param>
    public LowBatteryNotifier(Action<string, string> showToast, LowBatteryAlertStore? alertStore)
    {
        _showToast = showToast;
        _alertStore = alertStore;
        if (alertStore is not null) _policy.Restore(alertStore.Load());
        _saved = _policy.Alerted;
    }

    public int Threshold
    {
        get => _policy.Threshold;
        set => _policy.Threshold = value;
    }

    public bool Enabled { get; set; } = true;

    public void Process(IReadOnlyList<DeviceState> snapshot)
    {
        var alerts = _policy.Evaluate(snapshot);
        SaveAlertedIfChanged();
        if (alerts.Count == 0) return;

        var messages = alerts.Select(DeviceText.LowBatteryMessage).ToList();
        foreach (var message in messages) Log.Info($"{Title}: {message}{(Enabled ? "" : " (notifications are off)")}");
        if (Enabled) _showToast(Title, string.Join('\n', messages));
    }

    private void SaveAlertedIfChanged()
    {
        var alerted = _policy.Alerted;
        if (_alertStore is null || alerted.SetEquals(_saved)) return;
        _alertStore.Save(alerted);
        _saved = alerted;
    }
}
