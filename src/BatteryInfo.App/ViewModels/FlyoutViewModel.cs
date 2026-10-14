using System.Collections.ObjectModel;
using System.Globalization;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Settings;

namespace BatteryInfo.App.ViewModels;

/// <summary>The flyout: device rows plus the footer (last update, refresh, threshold, notifications, autostart).
/// Use from the UI thread.</summary>
public sealed class FlyoutViewModel : ObservableObject
{
    public static readonly TimeSpan HistoryRange = TimeSpan.FromDays(7);
    private static readonly int[] StandardThresholds = [10, 15, 20, 25, 30];

    private readonly IFlyoutServices _services;
    private readonly RelayCommand _refreshCommand;
    private IReadOnlyList<DeviceState> _snapshot = [];
    private DateTimeOffset? _lastRefresh;
    private string _updatedText = "Not updated yet";
    private bool _isRefreshing;
    private int _threshold;
    private bool _notificationsEnabled;
    private bool _startWithWindows;

    public FlyoutViewModel(IFlyoutServices services, AppSettings settings)
    {
        _services = services;
        _threshold = settings.LowBatteryThreshold;
        _notificationsEnabled = settings.NotificationsEnabled;
        _startWithWindows = services.IsStartWithWindowsEnabled();
        _refreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsRefreshing);
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
            if (existing.IsExpanded) LoadHistory(existing, now);
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

    // One row open at a time keeps the flyout short.
    private void OnExpandedChanged(DeviceViewModel device)
    {
        if (!device.IsExpanded) return;
        foreach (var other in Devices.Where(d => d != device)) other.IsExpanded = false;
        LoadHistory(device, _services.Now);
    }

    private void LoadHistory(DeviceViewModel device, DateTimeOffset now)
    {
        var from = now - HistoryRange;
        device.SetHistory(_services.LoadHistory(device.Id, from), from, now);
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
