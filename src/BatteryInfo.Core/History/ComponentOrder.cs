namespace BatteryInfo.Core.History;

/// <summary>Display order of battery components: the single battery, then Left, Right, Case, then anything else.</summary>
internal static class ComponentOrder
{
    public static int Rank(string component) => component switch
    {
        "" => 0,
        _ when component.Equals("Left", StringComparison.OrdinalIgnoreCase) => 1,
        _ when component.Equals("Right", StringComparison.OrdinalIgnoreCase) => 2,
        _ when component.Equals("Case", StringComparison.OrdinalIgnoreCase) => 3,
        _ => 4,
    };
}
