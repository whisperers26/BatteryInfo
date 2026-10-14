using System.Globalization;
using System.Text.RegularExpressions;

namespace BatteryInfo.Core.Hid;

/// <summary>One HID top-level collection (one openable path).</summary>
/// <param name="InterfaceNumber">USB interface number from "&amp;mi_XX" in the path, if any.</param>
/// <param name="InstanceId">The HID devnode, e.g. HID\VID_046D&amp;PID_C54D&amp;MI_02&amp;COL01\C&amp;1823A09C&amp;0&amp;0000.</param>
/// <param name="ParentInstanceId">Its parent devnode, e.g. USB\VID_046D&amp;PID_C54D&amp;MI_02\B&amp;18153BD2&amp;0&amp;0002.
/// Collections of one USB interface share it.</param>
/// <param name="OutputReportId">Report id of the first output report in the caps, null if there is none.</param>
/// <param name="FeatureReportId">Report id of the first feature report in the caps, null if there is none.</param>
/// <param name="Accessible">A zero-access open and the caps queries succeeded. When false only the path,
/// ids and instance ids are known.</param>
public sealed partial record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort Version,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength,
    int? InterfaceNumber,
    string? InstanceId,
    string? ParentInstanceId,
    string? Product,
    string? Serial,
    byte? OutputReportId,
    byte? FeatureReportId,
    bool Accessible)
{
    /// <summary>Short label for logs, e.g. "046D:C54D FF00/0001".</summary>
    public string Label => $"{VendorId:X4}:{ProductId:X4} {UsagePage:X4}/{Usage:X4}";

    public static int? ParseInterfaceNumber(string path)
    {
        var match = InterfaceRegex().Match(path);
        return match.Success ? int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Reads VID/PID from a USB (vid_046d&amp;pid_c54d), classic Bluetooth (vid&amp;0002046d_pid&amp;b023)
    /// or BLE (vid&amp;02068e_pid&amp;00d8) HID path.</summary>
    public static (ushort VendorId, ushort ProductId)? ParseVendorProduct(string path)
    {
        var vid = VendorRegex().Match(path);
        var pid = ProductRegex().Match(path);
        if (!vid.Success || !pid.Success) return null;
        var vidHex = vid.Groups[1].Value[^4..];
        return (ushort.Parse(vidHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            ushort.Parse(pid.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex("&mi_([0-9a-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex InterfaceRegex();

    [GeneratedRegex("vid[_&]([0-9a-f]{4,8})", RegexOptions.IgnoreCase)]
    private static partial Regex VendorRegex();

    [GeneratedRegex("pid[_&]([0-9a-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex ProductRegex();
}
