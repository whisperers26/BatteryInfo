using System.Globalization;
using BatteryInfo.Core.Hid;

namespace BatteryInfo.Cli;

internal static class Program
{
    private const string Usage = """
        usage: batteryinfo <command> [options]

          hid [--vid XXXX]      list HID collections (ids, usages, report lengths, report ids, access)
        """;

    private static int Main(string[] args)
    {
        var command = args.FirstOrDefault();
        var options = args.Skip(1).ToArray();
        switch (command)
        {
            case "hid":
                return ListHid(options);
            default:
                Console.Error.WriteLine(Usage);
                return command is null or "-h" or "--help" ? 0 : 2;
        }
    }

    private static int ListHid(string[] options)
    {
        ushort? vid = Option(options, "--vid") is { } text ? ushort.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
        var devices = new WindowsHidBackend().Enumerate(vid is null ? null : (v, _) => v == vid);
        foreach (var d in devices.OrderBy(d => d.VendorId).ThenBy(d => d.ProductId).ThenBy(d => d.Path, StringComparer.OrdinalIgnoreCase))
        {
            var mi = d.InterfaceNumber is { } n ? $"mi{n:D2}" : "    ";
            var rid = $"out:{Hex(d.OutputReportId)} feat:{Hex(d.FeatureReportId)}";
            var access = d.Accessible ? "ok    " : "denied";
            Console.WriteLine($"{d.VendorId:X4}:{d.ProductId:X4} {mi} {d.UsagePage:X4}/{d.Usage:X4} " +
                              $"in:{d.InputReportLength,-3} out:{d.OutputReportLength,-3} feat:{d.FeatureReportLength,-3} {rid} {access} " +
                              $"{d.Product ?? "-"} | {d.Serial ?? "-"} | {d.ParentInstanceId ?? "-"}");
        }
        Console.WriteLine($"{devices.Count} collection(s)");
        return 0;
    }

    private static string Hex(byte? value) => value is { } v ? v.ToString("X2", CultureInfo.InvariantCulture) : "--";

    private static string? Option(string[] options, string name)
    {
        var index = Array.IndexOf(options, name);
        return index >= 0 && index + 1 < options.Length ? options[index + 1] : null;
    }
}
