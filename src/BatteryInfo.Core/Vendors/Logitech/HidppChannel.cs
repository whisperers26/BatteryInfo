using System.Diagnostics;
using BatteryInfo.Core.Hid;

namespace BatteryInfo.Core.Vendors.Logitech;

/// <summary>The short (7-byte, usage FF00/0001) and long (20-byte, usage FF00/0002) HID++ collections of one
/// receiver or wired device. <see cref="Key"/> is shared by both (see <see cref="LogitechCommandPolicy.ChannelKey"/>).</summary>
internal sealed record HidppCollections(string Key, HidDeviceInfo Short, HidDeviceInfo Long);

/// <summary>Timeouts for HID++ exchanges (docs/protocols/logitech.md §4, §5, §6).</summary>
/// <param name="Register">Receiver register reads, answered by the receiver in about 4 ms.</param>
/// <param name="Ping">IRoot ping; no reply within it means the device is asleep or off.</param>
/// <param name="Feature">HID++ 2.0 feature calls, routed to the device (5-42 ms seen).</param>
/// <param name="Slice">How long to wait on the handle the answer is expected on before checking the other one.</param>
/// <param name="BusyRetryDelay">Wait before retrying a request the device answered with BUSY.</param>
internal sealed record HidppTimings(TimeSpan Register, TimeSpan Ping, TimeSpan Feature, TimeSpan Slice, TimeSpan BusyRetryDelay)
{
    public static HidppTimings Default { get; } = new(
        Register: TimeSpan.FromSeconds(1),
        Ping: TimeSpan.FromSeconds(2.5),
        Feature: TimeSpan.FromSeconds(2),
        Slice: TimeSpan.FromMilliseconds(15),
        BusyRetryDelay: TimeSpan.FromMilliseconds(100));
}

/// <summary>The answer to one request. <see cref="HidppMatchKind.None"/> means no answer arrived in time.</summary>
internal readonly record struct HidppResult(HidppMatchKind Kind, byte[]? Report, byte ErrorCode)
{
    public bool TimedOut => Kind == HidppMatchKind.None;
}

/// <summary>One request at a time over a pair of HID++ collections. Requests are written as short frames to the
/// short collection; answers do not come back on the handle written to (HID++ 2.0 replies and long-register replies
/// arrive on the long collection, 0x81 replies and every 0x8F error on the short one), so both are read. Reports that
/// do not answer the request (other software's replies, notifications, events) go to <see cref="Unsolicited"/>.
/// Each handle has its own input queue, so nothing is lost while the other one is being read.</summary>
internal sealed class HidppChannel(IHidConnection shortLink, IHidConnection longLink, TimeSpan slice) : IDisposable
{
    private const ushort HidppUsagePage = 0xFF00;

    public event Action<byte[]>? Unsolicited;

    /// <summary>Groups HID++ collections by receiver; only receivers with both an accessible short and long
    /// collection are returned, in enumeration order.</summary>
    public static IReadOnlyList<HidppCollections> Pair(IEnumerable<HidDeviceInfo> collections) =>
        collections
            .Where(c => c.Accessible && c.UsagePage == HidppUsagePage)
            .GroupBy(LogitechCommandPolicy.ChannelKey)
            .Select(g => (g.Key, Short: g.FirstOrDefault(IsShort), Long: g.FirstOrDefault(IsLong)))
            .Where(g => g.Short is not null && g.Long is not null)
            .Select(g => new HidppCollections(g.Key, g.Short!, g.Long!))
            .ToList();

    /// <summary>Writes <paramref name="request"/> (a short frame) and waits for its reply or error.</summary>
    public HidppResult Send(byte[] request, TimeSpan timeout)
    {
        shortLink.Write(request);

        // 0x81 replies come back short; everything else this app sends is answered on the long collection,
        // unless the receiver answers with a (short) 0x8F error.
        var (expected, other) = request[2] == HidppProtocol.GetRegister ? (shortLink, longLink) : (longLink, shortLink);
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var remaining = timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero) return default;
            if (Check(request, expected.Read(remaining < slice ? remaining : slice)) is { } first) return first;
            if (Check(request, other.Read(TimeSpan.Zero)) is { } second) return second;
        }
    }

    public void Dispose()
    {
        shortLink.Dispose();
        longLink.Dispose();
    }

    private HidppResult? Check(byte[] request, byte[]? report)
    {
        if (report is null) return null;
        var match = HidppProtocol.Match(request, report);
        if (match.Kind != HidppMatchKind.None) return new HidppResult(match.Kind, report, match.ErrorCode);
        Unsolicited?.Invoke(report);
        return null;
    }

    private static bool IsShort(HidDeviceInfo c) =>
        c.Usage == 0x0001 && c.InputReportLength == HidppProtocol.ShortLength && c.OutputReportLength == HidppProtocol.ShortLength;

    private static bool IsLong(HidDeviceInfo c) =>
        c.Usage == 0x0002 && c.InputReportLength == HidppProtocol.LongLength && c.OutputReportLength == HidppProtocol.LongLength;
}
