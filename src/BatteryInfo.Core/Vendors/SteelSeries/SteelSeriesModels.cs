namespace BatteryInfo.Core.Vendors.SteelSeries;

/// <summary>A SteelSeries headset read through its wireless base station.</summary>
/// <param name="ProductId">PID of the base station (VID 0x1038).</param>
public sealed record SteelSeriesModel(string Name, ushort ProductId)
{
    public string DeviceId => $"steelseries:{ProductId:X4}";

    public string DisplayName => $"SteelSeries {Name}";
}

/// <summary>The headsets this app may talk to. Only the Nova 5X (2253) was tested on hardware; the Nova 5 shares its
/// protocol in HeadsetControl.</summary>
public static class SteelSeriesModels
{
    public const ushort VendorId = 0x1038;

    public static IReadOnlyList<SteelSeriesModel> All { get; } =
    [
        new("Arctis Nova 5", 0x2232),
        new("Arctis Nova 5X", 0x2253),
    ];

    public static SteelSeriesModel? Find(ushort vendorId, ushort productId) =>
        vendorId == VendorId ? All.FirstOrDefault(m => m.ProductId == productId) : null;
}
