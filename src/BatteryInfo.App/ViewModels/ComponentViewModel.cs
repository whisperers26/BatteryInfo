using BatteryInfo.Core.Model;
using BatteryInfo.Core.Presentation;

namespace BatteryInfo.App.ViewModels;

public enum BatteryLevel
{
    Normal,
    Low,
    Charging,
    Unknown,
}

/// <summary>One battery value in a device row: label, percentage and bar.</summary>
public sealed class ComponentViewModel(string name) : ObservableObject
{
    private string _label = "";
    private string _percentText = "";
    private double _fraction;
    private BatteryLevel _level;
    private bool _isCharging;
    private bool _isCompact;

    public string Name { get; } = name;

    /// <summary>"" for a single battery, otherwise "L", "R" or "Case".</summary>
    public string Label
    {
        get => _label;
        private set
        {
            if (SetProperty(ref _label, value)) OnPropertyChanged(nameof(HasLabel));
        }
    }

    public bool HasLabel => Label.Length > 0;

    public string PercentText
    {
        get => _percentText;
        private set => SetProperty(ref _percentText, value);
    }

    /// <summary>The bar's fill, 0 to 1.</summary>
    public double Fraction
    {
        get => _fraction;
        private set => SetProperty(ref _fraction, value);
    }

    public BatteryLevel Level
    {
        get => _level;
        private set => SetProperty(ref _level, value);
    }

    public bool IsCharging
    {
        get => _isCharging;
        private set => SetProperty(ref _isCharging, value);
    }

    /// <summary>True when the device shows several values side by side (earbuds), so each bar is shorter.</summary>
    public bool IsCompact
    {
        get => _isCompact;
        set => SetProperty(ref _isCompact, value);
    }

    public void Update(BatteryComponent component, bool approximate, int lowThreshold)
    {
        Label = DeviceText.ComponentLabel(component.Name);
        PercentText = DeviceText.Percent(component.Percent, approximate);
        Fraction = Math.Clamp(component.Percent ?? 0, 0, 100) / 100.0;
        IsCharging = component.Charge == ChargeState.Charging;
        Level = component.Percent switch
        {
            null => BatteryLevel.Unknown,
            _ when IsCharging => BatteryLevel.Charging,
            var p when p <= lowThreshold => BatteryLevel.Low,
            _ => BatteryLevel.Normal,
        };
    }
}
