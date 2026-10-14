using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BatteryInfo.App.Flyout;
using BatteryInfo.App.Interop;
using BatteryInfo.App.Tray;
using BatteryInfo.App.ViewModels;
using BatteryInfo.Core;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.History;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Presentation;
using BatteryInfo.Core.Settings;
using Microsoft.Win32;

namespace BatteryInfo.App;

/// <summary>Composition root: one tray process that polls the providers, keeps history and shows the flyout.</summary>
public partial class App : Application, IFlyoutServices
{
    private const string InstanceName = "BatteryInfo";
    private static readonly TimeSpan RefreshOnOpenAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HistoryKeep = TimeSpan.FromDays(90);
    private static readonly TimeSpan ResumeRefreshDelay = TimeSpan.FromSeconds(5);
    private static readonly bool DpiAwarenessSet;

    private readonly TimeProvider _time = TimeProvider.System;
    private StartupOptions _options = new(false, false, false);
    private SingleInstance? _instance;
    private SettingsStore? _settingsStore;
    private AppSettings _settings = new();
    private HistoryStore? _history;
    private BatteryMonitor? _monitor;
    private TrayIconHost? _tray;
    private FlyoutViewModel? _flyoutViewModel;
    private FlyoutWindow? _flyout;

    // Before WPF or WinForms create any window, so both lay out per monitor. This constructor must not mention
    // other WPF types: compiling it would load WPF, which fixes the DPI awareness first.
    static App() => DpiAwarenessSet = NativeMethods.EnablePerMonitorDpiAwareness();

    protected override void OnStartup(StartupEventArgs e)
    {
        // Hardware-rendered WPF content stays blank on indirect (virtual) displays, such as the streaming display
        // this PC uses as its primary screen. The flyout is small, so software rendering costs nothing noticeable.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);
        _options = StartupOptions.Parse(e.Args);
        var instanceName = _options.Demo ? InstanceName + ".Demo" : InstanceName;
        _instance = SingleInstance.TryAcquire(instanceName);
        if (_instance is null)
        {
            if (!_options.Tray) SingleInstance.RequestShow(instanceName);
            Shutdown();
            return;
        }

        var root = DataRoot(_options.Demo);
        var logs = Path.Combine(root, "logs");
        Log.Configure(logs, traceFrames: false);
        _settingsStore = new SettingsStore(Path.Combine(root, "settings.json"));
        _settings = _settingsStore.Load();
        if (_settings.TraceFrames) Log.Configure(logs, traceFrames: true);
        Log.Info($"BatteryInfo starting (args: {string.Join(' ', e.Args)}; per-monitor DPI set: {DpiAwarenessSet})");
        HookUnhandledExceptions();

        _history = new HistoryStore(Path.Combine(root, "history"), _time);
        _history.Prune(HistoryKeep);
        var store = new DeviceStore(_time);
        foreach (var reading in _history.LastKnownReadings()) store.Seed(reading);

        var providers = ProviderFactory.CreateAll();
        _monitor = new BatteryMonitor(providers, store, new CmDeviceChangeWatcher(), _time,
            TimeSpan.FromSeconds(_settings.PollIntervalSeconds), ProviderTimeout);
        _monitor.SnapshotChanged += OnSnapshotChanged;

        _flyoutViewModel = new FlyoutViewModel(this, _settings);
        _tray = new TrayIconHost(Dispatcher);
        _tray.LeftClicked += (_, _) => ToggleFlyout();
        _tray.RefreshClicked += (_, _) => _ = _monitor.RefreshAsync();
        _tray.ExitClicked += (_, _) => Shutdown();
        _instance.ListenForShowRequests(() => Dispatcher.BeginInvoke(ShowFlyout));

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        ApplySnapshot(store.Snapshot());
        _ = _monitor.Start();
        if (_options.ShowFlyout) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ShowFlyout);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _flyout?.CloseForExit();
        _tray?.Dispose();
        if (_monitor is { } monitor)
        {
            monitor.SnapshotChanged -= OnSnapshotChanged;
            try
            {
                // Off the UI thread: the monitor's awaits must not need this dispatcher while it is blocked here.
                Task.Run(() => monitor.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(3));
            }
            catch (AggregateException ex)
            {
                Log.Error("Stopping the monitor failed", ex);
            }
        }
        _instance?.Dispose();
        if (_instance is not null) Log.Info("BatteryInfo stopped");
        base.OnExit(e);
    }

    private static string DataRoot(bool demo)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryInfo");
        return demo ? Path.Combine(root, "demo") : root;
    }

    private void HookUnhandledExceptions()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true; // keep the tray icon alive
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    // Raised on a pool thread after every poll: write history there, update the UI on the dispatcher.
    private void OnSnapshotChanged(object? sender, IReadOnlyList<DeviceState> snapshot)
    {
        _history?.Append(snapshot);
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(IReadOnlyList<DeviceState> snapshot)
    {
        _tray?.SetTooltip(TrayTooltip.Build(snapshot));
        _flyoutViewModel?.Update(snapshot, _monitor?.LastRefresh);
    }

    private void ToggleFlyout()
    {
        if (_flyout is { IsVisible: true })
        {
            _flyout.HideFlyout();
            return;
        }
        if (_flyout?.JustHidden == true) return;
        ShowFlyout();
    }

    private void ShowFlyout()
    {
        if (_flyoutViewModel is null || _monitor is null) return;
        _flyout ??= new FlyoutWindow(_flyoutViewModel);
        _flyoutViewModel.ReloadStartWithWindows();
        _flyoutViewModel.Rebuild();
        _flyout.ShowNearTray();
        if (_monitor.LastRefresh is not { } last || _time.GetUtcNow() - last > RefreshOnOpenAfter)
        {
            _flyoutViewModel.RefreshCommand.Execute(null);
        }
    }

    private void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _settingsStore?.Save(settings);
        Log.Info($"Settings changed: {settings}");
    }

    DateTimeOffset IFlyoutServices.Now => _time.GetLocalNow();

    Task IFlyoutServices.RefreshAsync() => _monitor?.RefreshAsync() ?? Task.CompletedTask;

    IReadOnlyList<HistorySeries> IFlyoutServices.LoadHistory(string deviceId, DateTimeOffset since)
    {
        if (_history is null) return [];
        // A gap longer than the heartbeat plus a few polls means the device was not read: break the line there.
        var maxGap = HistoryStore.Heartbeat + 3 * TimeSpan.FromSeconds(_settings.PollIntervalSeconds);
        return HistorySeries.Build(_history.Query(deviceId, since), maxGap);
    }

    bool IFlyoutServices.IsStartWithWindowsEnabled() => false;

    void IFlyoutServices.SetStartWithWindows(bool enabled)
    {
    }

    void IFlyoutServices.SetLowBatteryThreshold(int percent) => UpdateSettings(_settings with { LowBatteryThreshold = percent });

    void IFlyoutServices.SetNotificationsEnabled(bool enabled) => UpdateSettings(_settings with { NotificationsEnabled = enabled });

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume || _monitor is not { } monitor) return;
        Log.Info($"Resumed from sleep; refreshing in {ResumeRefreshDelay.TotalSeconds:0} s");
        _ = Task.Delay(ResumeRefreshDelay).ContinueWith(_ => monitor.RefreshAsync(), TaskScheduler.Default).Unwrap();
    }
}
