using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BatteryInfo.Core;
using BatteryInfo.Core.Bluetooth;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Hid;
using BatteryInfo.Core.Model;
using BatteryInfo.Core.Vendors.Apple;

namespace BatteryInfo.Cli;

internal static class Program
{
    private const string Usage = """
        usage: batteryinfo <command> [options]

          list [--vendor <name>] [--json] [--trace]
                                read every device's battery once (--trace prints device frames to stderr)
          hid [--vid XXXX]      list HID collections (ids, usages, report lengths, report ids, access)
          bt-battery <address>  Windows' own battery value for a Bluetooth address (12 hex digits)
          apple-scan [seconds]  listen (passively, sending nothing) for AirPods and Beats battery broadcasts
                                and print each one decoded; 30 s by default
        """;

    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(15);

    private static async Task<int> Main(string[] args)
    {
        var command = args.FirstOrDefault();
        var options = args.Skip(1).ToArray();
        switch (command)
        {
            case "list":
                return await ListReadings(options);
            case "hid":
                return ListHid(options);
            case "bt-battery" when options.Length == 1:
                return BluetoothBattery(options[0]);
            case "apple-scan" when options.Length <= 1:
                return await AppleScan(options);
            default:
                Console.Error.WriteLine(Usage);
                return command is null or "-h" or "--help" ? 0 : 2;
        }
    }

    private static async Task<int> ListReadings(string[] options)
    {
        var trace = options.Contains("--trace");
        Log.Configure(null, trace);
        Log.LineWritten += line => Console.Error.WriteLine(line);

        var vendor = Option(options, "--vendor");
        var readings = new List<DeviceReading>();
        foreach (var provider in ProviderFactory.CreateAll(oneShot: true))
        {
            using (provider)
            {
                if (vendor is not null && !provider.Vendor.Equals(vendor, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    readings.AddRange(await provider.PollAsync(CancellationToken.None).WaitAsync(ProviderTimeout));
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"{provider.Vendor}: {e.Message}");
                }
            }
        }

        if (options.Contains("--json"))
        {
            var json = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            Console.WriteLine(JsonSerializer.Serialize(readings, json));
        }
        else
        {
            foreach (var r in readings)
            {
                var detail = r.Detail is null ? "" : $"  ({r.Detail})";
                Console.WriteLine($"{r.Vendor,-9} {r.Name,-26} {r.Status,-11} {FormatComponents(r),-34} {r.Source}{detail}");
            }
            Console.WriteLine(readings.Count == 0 ? "no devices found" : $"{readings.Count} device(s)");
        }
        return 0;
    }

    private static string FormatComponents(DeviceReading reading) => FormatComponents(reading.Components, reading.Approximate);

    private static string FormatComponents(IReadOnlyList<BatteryComponent> components, bool approximate)
    {
        if (components.Count == 0) return "-";
        var approx = approximate ? "~" : "";
        return string.Join(" | ", components.Select(c =>
        {
            var name = c.Name.Length == 0 ? "" : $"{c.Name} ";
            var percent = c.Percent is { } p ? $"{approx}{p}%" : "?";
            var charge = c.Charge is ChargeState.Unknown or ChargeState.Discharging ? "" : $" {c.Charge.ToString().ToLowerInvariant()}";
            return $"{name}{percent}{charge}";
        }));
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

    private static async Task<int> AppleScan(string[] options)
    {
        var seconds = 30;
        if (options.Length == 1 && (!int.TryParse(options[0], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds < 1))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        using var bluetooth = new WindowsAppleBluetooth();
        var paired = bluetooth.FindPaired();
        foreach (var device in paired)
        {
            var model = AppleModels.Find(device.ModelId)?.Name ?? "model not supported yet";
            var state = bluetooth.IsConnected(device.Address) ? "connected" : "not connected";
            Console.WriteLine($"paired: {device.Name} {BluetoothBatteryProperty.FormatAddress(device.Address)} PID 0x{device.ModelId:X4} ({model}), {state}");
        }
        if (paired.Count == 0) Console.WriteLine("no AirPods or Beats paired with this PC");

        var frames = 0;
        var gate = new object();
        bluetooth.AdvertisementReceived += (_, advertisement) =>
        {
            if (advertisement.Data.Length == 0 || advertisement.Data[0] != AppleProximityProtocol.ProximityPairingType) return;
            lock (gate)
            {
                frames++;
                Console.WriteLine(DescribeProximityFrame(advertisement));
            }
        };
        bluetooth.ListeningFailed += (_, reason) => Console.Error.WriteLine($"the scan stopped: {reason}");
        bluetooth.StartListening();
        Console.WriteLine($"listening for {seconds} s (passive scan: nothing is sent) ...");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        bluetooth.StopListening();
        lock (gate) Console.WriteLine($"{frames} Proximity Pairing frame(s)");
        return 0;
    }

    // Bytes 0-10 only: the rest is encrypted.
    private static string DescribeProximityFrame(BleAdvertisement advertisement)
    {
        var data = advertisement.Data;
        var plaintext = Log.Hex(data.AsSpan(0, Math.Min(data.Length, AppleProximityProtocol.PlaintextLength)));
        var address = BluetoothBatteryProperty.FormatAddress(advertisement.Address) + (advertisement.PublicAddress ? " public" : "");
        var head = $"{DateTime.Now:HH:mm:ss.fff} {address} {advertisement.Rssi,4} dBm {advertisement.Kind,-24} {plaintext}";
        if (AppleProximityProtocol.Parse(data) is not { } status) return $"{head}  (not a status frame)";
        if (AppleModels.Find(status.ModelId) is not { } model) return $"{head}  PID 0x{status.ModelId:X4} (model not supported yet)";
        return $"{head}  {model.Name}: {FormatComponents(model.Components(status), approximate: true)}";
    }

    private static int BluetoothBattery(string address)
    {
        var value = BluetoothBatteryProperty.Read(ulong.Parse(address.Replace(":", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        Console.WriteLine(value is null ? "no battery value" : $"{value} %");
        return value is null ? 1 : 0;
    }

    private static string Hex(byte? value) => value is { } v ? v.ToString("X2", CultureInfo.InvariantCulture) : "--";

    private static string? Option(string[] options, string name)
    {
        var index = Array.IndexOf(options, name);
        return index >= 0 && index + 1 < options.Length ? options[index + 1] : null;
    }
}
