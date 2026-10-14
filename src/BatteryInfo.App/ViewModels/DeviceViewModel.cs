using System.Collections.ObjectModel;
using BatteryInfo.Core.History;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Monitoring;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.App.ViewModels;

/// <summary>One row of the flyout.</summary>
public sealed class DeviceViewModel : ObservableObject
{
    private string _name = "";
    private string _kindGlyph = "";
    private string _statusText = "";
    private string _tooltip = "";
    private bool _isDimmed;
    private bool _isExpanded;
    private IReadOnlyList<HistorySeries> _history = [];
    private DateTimeOffset _historyFrom;
    private DateTimeOffset _historyTo;

    private readonly Action<DeviceViewModel> _expandedChanged;

    public DeviceViewModel(string id, Action<DeviceViewModel> expandedChanged)
    {
        Id = id;
        _expandedChanged = expandedChanged;
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    /// <summary>A Segoe Fluent Icons character for the kind of device.</summary>
    public string KindGlyph
    {
        get => _kindGlyph;
        private set => SetProperty(ref _kindGlyph, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string Tooltip
    {
        get => _tooltip;
        private set => SetProperty(ref _tooltip, value);
    }

    /// <summary>The values shown are not live (asleep, not connected, no access).</summary>
    public bool IsDimmed
    {
        get => _isDimmed;
        private set => SetProperty(ref _isDimmed, value);
    }

    /// <summary>Shows the history chart. Expanding one row collapses the others.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value)) _expandedChanged(this);
        }
    }

    public ObservableCollection<ComponentViewModel> Components { get; } = [];

    /// <summary>Earbuds and the like: the values get a line of their own under the name.</summary>
    public bool IsMultiComponent => Components.Count > 1;

    public IReadOnlyList<HistorySeries> History
    {
        get => _history;
        private set => SetProperty(ref _history, value);
    }

    public DateTimeOffset HistoryFrom
    {
        get => _historyFrom;
        private set => SetProperty(ref _historyFrom, value);
    }

    public DateTimeOffset HistoryTo
    {
        get => _historyTo;
        private set => SetProperty(ref _historyTo, value);
    }

    public static string GlyphFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "",
        DeviceKind.Keyboard => "",
        DeviceKind.Earbuds or DeviceKind.Headset => "",
        DeviceKind.Controller => "",
        _ => "",
    };

    public void Update(DeviceState state, DateTimeOffset now, int lowThreshold)
    {
        var latest = state.Latest;
        Name = latest.Name;
        KindGlyph = GlyphFor(latest.Kind);
        StatusText = DeviceText.StatusLine(state, now);
        IsDimmed = latest.Status != DeviceStatus.Online;
        var shownReading = latest.HasBattery ? latest : state.LastWithBattery;
        var approximate = shownReading?.Approximate ?? false;
        Tooltip = BuildTooltip(state, shownReading, now);

        var components = state.DisplayComponents;
        if (components.Count == 0) components = [BatteryComponent.Single(null, ChargeState.Unknown)];
        if (!Components.Select(c => c.Name).SequenceEqual(components.Select(c => c.Name)))
        {
            Components.Clear();
            foreach (var component in components) Components.Add(new ComponentViewModel(component.Name));
            OnPropertyChanged(nameof(IsMultiComponent));
        }
        for (var i = 0; i < components.Count; i++)
        {
            Components[i].IsCompact = components.Count > 1;
            Components[i].Update(components[i], approximate, lowThreshold);
        }
    }

    public void SetHistory(IReadOnlyList<HistorySeries> history, DateTimeOffset from, DateTimeOffset to)
    {
        HistoryFrom = from;
        HistoryTo = to;
        History = history;
    }

    private static string BuildTooltip(DeviceState state, DeviceReading? shownReading, DateTimeOffset now)
    {
        var latest = state.Latest;
        var lines = new List<string> { $"{latest.Vendor} · read via {latest.Source}" };
        if (state.BatteryTimestamp is { } readAt) lines.Add($"Battery read {RelativeTime.Format(readAt, now)}");
        if (shownReading is not null && DeviceText.ApproximateNote(shownReading) is { } note) lines.Add(note);
        if (latest.Detail is { Length: > 0 } detail && !lines.Contains(detail)) lines.Add(detail);
        lines.Add("Click for the 7-day history");
        return string.Join('\n', lines);
    }
}
