namespace BatteryInfo.Core.Model;

public enum ChargeState
{
    /// <summary>The device did not report a charging state.</summary>
    Unknown,
    Discharging,
    Charging,
    Full,

    /// <summary>On external power but not charging (held at full, charge limit reached).</summary>
    NotCharging,

    /// <summary>The device reported a charging error.</summary>
    Fault,
}
