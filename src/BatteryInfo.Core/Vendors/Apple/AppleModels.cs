using BatteryInfo.Core.Model;

namespace BatteryInfo.Core.Vendors.Apple;

public enum AppleLayout
{
    /// <summary>Two earbuds and a charging case.</summary>
    Earbuds,

    /// <summary>Two earbuds and a case that has no battery of its own.</summary>
    EarbudsWithoutCaseBattery,

    /// <summary>One battery (headphones, neckbands): its level is the low nibble of byte 6.</summary>
    OneBattery,
}

/// <summary>An AirPods or Beats model, by the PID it broadcasts and pairs with (docs/protocols/apple.md §6).</summary>
/// <param name="ReportsCharging">The charging bits mean something for this model: all earbuds, and the AirPods Max
/// family among the one-battery models.</param>
public sealed record AppleModel(ushort Id, string Name, AppleLayout Layout, DeviceKind Kind, bool ReportsCharging)
{
    private const string Left = "Left";
    private const string Right = "Right";
    private const string Case = "Case";

    /// <summary>The names of the batteries <see cref="Components"/> reports, in order.</summary>
    public IReadOnlyList<string> ComponentNames => Layout switch
    {
        AppleLayout.OneBattery => [""],
        AppleLayout.EarbudsWithoutCaseBattery => [Left, Right],
        _ => [Left, Right, Case],
    };

    /// <summary>The batteries one status frame reports: Left, Right and (when the case has a battery) Case, or a single
    /// one. A level not reported is null, and charging is only shown for a reported level.</summary>
    public IReadOnlyList<BatteryComponent> Components(ProximityStatus status)
    {
        if (Layout == AppleLayout.OneBattery)
        {
            return [BatteryComponent.Single(AppleProximityProtocol.Percent(status.ThisLevel), Charge(status.ThisLevel, status.ThisCharging))];
        }

        var left = new BatteryComponent(Left, AppleProximityProtocol.Percent(status.LeftLevel), Charge(status.LeftLevel, status.LeftCharging));
        var right = new BatteryComponent(Right, AppleProximityProtocol.Percent(status.RightLevel), Charge(status.RightLevel, status.RightCharging));
        if (Layout == AppleLayout.EarbudsWithoutCaseBattery) return [left, right];
        return [left, right, new BatteryComponent(Case, AppleProximityProtocol.Percent(status.CaseLevel), Charge(status.CaseLevel, status.CaseCharging))];
    }

    // A clear bit does not mean discharging: the frame cannot tell a worn pod from one that is full in its case.
    private ChargeState Charge(int level, bool charging) =>
        ReportsCharging && charging && AppleProximityProtocol.Percent(level) is not null ? ChargeState.Charging : ChargeState.Unknown;
}

/// <summary>The models whose broadcast layout is known. Layouts follow capod's device classes and
/// Bluetooth-Battery-Meter's model configs, which agree on every one.</summary>
public static class AppleModels
{
    private static readonly AppleModel[] Models =
    [
        Earbuds(0x2002, "AirPods (1st generation)"),
        Earbuds(0x200F, "AirPods (2nd generation)"),
        Earbuds(0x2013, "AirPods (3rd generation)"),
        Earbuds(0x2019, "AirPods 4"),
        Earbuds(0x201B, "AirPods 4 (ANC)"),
        Earbuds(0x2030, "AirPods 5"),
        Earbuds(0x2036, "AirPods 5"),
        Earbuds(0x200E, "AirPods Pro"),
        Earbuds(0x2014, "AirPods Pro 2"),
        Earbuds(0x2024, "AirPods Pro 2 (USB-C)"),
        Earbuds(0x2027, "AirPods Pro 3"),
        new(0x200A, "AirPods Max", AppleLayout.OneBattery, DeviceKind.Headset, ReportsCharging: true),
        new(0x201F, "AirPods Max (USB-C)", AppleLayout.OneBattery, DeviceKind.Headset, ReportsCharging: true),
        new(0x202D, "AirPods Max 2", AppleLayout.OneBattery, DeviceKind.Headset, ReportsCharging: true),
        Earbuds(0x200B, "Powerbeats Pro"),
        Earbuds(0x201D, "Powerbeats Pro 2"),
        Earbuds(0x202F, "Powerbeats Fit"),
        Earbuds(0x2012, "Beats Fit Pro"),
        Earbuds(0x2011, "Beats Studio Buds"),
        Earbuds(0x2016, "Beats Studio Buds+"),
        new(0x2026, "Beats Solo Buds", AppleLayout.EarbudsWithoutCaseBattery, DeviceKind.Earbuds, ReportsCharging: true),
        OneBattery(0x2003, "Powerbeats3", DeviceKind.Earbuds),
        OneBattery(0x2005, "BeatsX", DeviceKind.Earbuds),
        OneBattery(0x2006, "Beats Solo3", DeviceKind.Headset),
        OneBattery(0x2009, "Beats Studio3", DeviceKind.Headset),
        OneBattery(0x200C, "Beats Solo Pro", DeviceKind.Headset),
        OneBattery(0x200D, "Powerbeats 4", DeviceKind.Earbuds),
        OneBattery(0x2010, "Beats Flex", DeviceKind.Earbuds),
        OneBattery(0x2017, "Beats Studio Pro", DeviceKind.Headset),
        OneBattery(0x2025, "Beats Solo 4", DeviceKind.Headset),
        OneBattery(0x2038, "Beats 360", DeviceKind.Earbuds),
    ];

    public static IReadOnlyList<AppleModel> All => Models;

    public static AppleModel? Find(ushort id) => Models.FirstOrDefault(m => m.Id == id);

    private static AppleModel Earbuds(ushort id, string name) => new(id, name, AppleLayout.Earbuds, DeviceKind.Earbuds, ReportsCharging: true);

    private static AppleModel OneBattery(ushort id, string name, DeviceKind kind) =>
        new(id, name, AppleLayout.OneBattery, kind, ReportsCharging: false);
}
