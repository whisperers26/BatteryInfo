using System.Collections.ObjectModel;
using System.Globalization;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Presentation;
using BatteryInfo.Core.Settings;
using BatteryInfo.Core.Updates;

namespace BatteryInfo.App.ViewModels;

/// <summary>The flyout: device rows plus the footer (last update, refresh, threshold, notifications, autostart).
/// Use from the UI thread.</summary>
public sealed class FlyoutViewModel : ObservableObject
{
    public static readonly TimeSpan HistoryRange = TimeSpan.FromDays(7);
    private static readonly int[] StandardThresholds = [10, 15, 20, 25, 30];

    private readonly IFlyoutServices _services;
    private readonly RelayCommand _refreshCommand;
    private readonly LatestLoad _historyLoad = new();
    private IReadOnlyList<DeviceState> _snapshot = [];
    private DateTimeOffset? _lastRefresh;
    private string _updatedText = "Not updated yet";
    private bool _isRefreshing;
    private int _threshold;
    private bool _notificationsEnabled;
    private bool _startWithWindows;
    private bool _isOpen;
    private bool _autoUpdate;
    private UpdateDescription _update = UpdateText.Describe(new UpdateStatus(UpdateState.Idle, null));
    private readonly RelayCommand _updateCommand;

    public FlyoutViewModel(IFlyoutServices services, AppSettings settings)
    {
        _services = services;
        _threshold = settings.LowBatteryThreshold;
        _notificationsEnabled = settings.NotificationsEnabled;
        _startWithWindows = services.IsStartWithWindowsEnabled();
        _autoUpdate = settings.AutoUpdate;
        _refreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsRefreshing);
        _updateCommand = new RelayCommand(services.RunUpdateAction, () => _update.ActionEnabled);
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    public bool HasDevices => Devices.Count > 0;

    public string UpdatedText
    {
        get => _updatedText;
        private set => SetProperty(ref _updatedText, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (!SetProperty(ref _isRefreshing, value)) return;
            _refreshCommand.RaiseCanExecuteChanged();
            UpdateUpdatedText();
        }
    }

    public RelayCommand RefreshCommand => _refreshCommand;

    public IReadOnlyList<int> ThresholdOptions =>
        StandardThresholds.Contains(_threshold) ? StandardThresholds : [.. StandardThresholds.Append(_threshold).Order()];

    public int Threshold
    {
        get => _threshold;
        set
        {
            if (!SetProperty(ref _threshold, value)) return;
            _services.SetLowBatteryThreshold(value);
            OnPropertyChanged(nameof(ThresholdOptions));
            Rebuild();
        }
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (SetProperty(ref _notificationsEnabled, value)) _services.SetNotificationsEnabled(value);
        }
    }

    public bool AutoUpdate
    {
        get => _autoUpdate;
        set
        {
            if (SetProperty(ref _autoUpdate, value)) _services.SetAutoUpdate(value);
        }
    }

    public string UpdateStatusText => _update.StatusLine;

    public string UpdateActionText => _update.ActionLabel;

    public RelayCommand UpdateCommand => _updateCommand;

    public void SetUpdateStatus(UpdateStatus status)
    {
        _update = UpdateText.Describe(status);
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(UpdateActionText));
        _updateCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Mirrors the Run registry value, which stays the source of truth.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (value == _startWithWindows) return;
            _services.SetStartWithWindows(value);
            ReloadStartWithWindows();
        }
    }

    /// <summary>Whether the flyout is on screen. An expanded row's history is read only while it is, and again
    /// each time it opens, so polls while it is hidden cost no disk reads.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            if (value && Devices.FirstOrDefault(d => d.IsExpanded) is { } expanded) LoadHistory(expanded, _services.Now);
        }
    }

    public void ReloadStartWithWindows()
    {
        _startWithWindows = _services.IsStartWithWindowsEnabled();
        OnPropertyChanged(nameof(StartWithWindows));
    }

    public void Update(IReadOnlyList<DeviceState> snapshot, DateTimeOffset? lastRefresh)
    {
        _snapshot = snapshot;
        _lastRefresh = lastRefresh;
        Rebuild();
    }

    /// <summary>Recomputes every row against the current time, e.g. so "14 min ago" is right when the flyout opens.</summary>
    public void Rebuild()
    {
        var now = _services.Now;
        for (var index = 0; index < _snapshot.Count; index++)
        {
            var state = _snapshot[index];
            var existing = Devices.FirstOrDefault(d => d.Id == state.Latest.DeviceId);
            if (existing is null)
            {
                existing = new DeviceViewModel(state.Latest.DeviceId, OnExpandedChanged);
                Devices.Insert(index, existing);
            }
            else if (Devices.IndexOf(existing) != index)
            {
                Devices.Move(Devices.IndexOf(existing), index);
            }
            existing.Update(state, now, _threshold);
            if (existing.IsExpanded && _isOpen) LoadHistory(existing, now);
        }
        while (Devices.Count > _snapshot.Count) Devices.RemoveAt(Devices.Count - 1);
        OnPropertyChanged(nameof(HasDevices));
        UpdateUpdatedText();
    }

    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await _services.RefreshAsync();
        }
        catch (Exception e)
        {
            Log.Error("Refresh from the flyout failed", e);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    // One row open at a time keeps the flyout short, so one load at a time serves every row: a collapse drops
    // the load still running, and the newest load wins.
    private void OnExpandedChanged(DeviceViewModel device)
    {
        if (!device.IsExpanded)
        {
            _historyLoad.Cancel();
            return;
        }
        foreach (var other in Devices.Where(d => d != device)) other.IsExpanded = false;
        if (_isOpen) LoadHistory(device, _services.Now);
    }

    // Reading and parsing the month files takes milliseconds that grow with the history: never on the UI thread.
    private void LoadHistory(DeviceViewModel device, DateTimeOffset now)
    {
        var from = now - HistoryRange;
        var id = device.Id;
        _ = _historyLoad.RunAsync(() => _services.LoadHistory(id, from), history => device.SetHistory(history, from, now));
    }

    private void UpdateUpdatedText()
    {
        UpdatedText = IsRefreshing
            ? "Updating…"
            : _lastRefresh is { } at
                ? "Updated " + at.ToOffset(_services.Now.Offset).ToString("t", CultureInfo.CurrentCulture)
                : "Not updated yet";
    }
}
