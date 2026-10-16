using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Presentation;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace BatteryInfo.App.Tray;

/// <summary>Shows real Windows toasts, which stay in Notification Center until dismissed. A tray balloon is turned
/// into a toast too, but Windows deletes it from Notification Center as soon as its banner closes, so a missed banner
/// left nothing behind. Toasts need the app id of a Start menu shortcut (Setup.exe makes one); without it, or when
/// Windows refuses the toast, this falls back to the balloon.</summary>
internal sealed class ToastSender(string? appUserModelId, Action<string, string> showBalloon)
{
    private ToastNotifier? _notifier;
    private bool _unavailable = appUserModelId is null;

    public void Show(string title, string text)
    {
        if (!_unavailable && Notifier() is { } notifier)
        {
            try
            {
                var xml = new XmlDocument();
                xml.LoadXml(ToastContent.Build(title, text));
                notifier.Show(new ToastNotification(xml));
                return;
            }
            catch (Exception e)
            {
                Log.Warn($"Could not show a toast as {appUserModelId}, using a tray balloon from now on: {e.Message}");
                _unavailable = true;
            }
        }
        showBalloon(title, text);
    }

    private ToastNotifier? Notifier()
    {
        if (_notifier is not null) return _notifier;
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier(appUserModelId);
            // Reading the setting throws for an app id no shortcut carries, where Show would drop the toast silently.
            Log.Info($"Toasts as {appUserModelId}: {notifier.Setting}");
            return _notifier = notifier;
        }
        catch (Exception e)
        {
            Log.Warn($"No toasts as {appUserModelId}, using tray balloons: {e.Message}");
            _unavailable = true;
            return null;
        }
    }
}
