using BatteryInfo.Core.Hid;

namespace BatteryInfo.Core.Vendors.Rog;

internal enum RogOutcome
{
    Reply,
    Rejected,
    NoReply,
}

internal readonly record struct RogExchange(RogOutcome Outcome, byte[]? Report);

/// <summary>Request/reply on one opened ROG collection (rog.md §3): flush, write the full output report, then read until
/// the matching reply, an FF AA reject or the timeout, skipping other programs' frames; one retry when nothing answers.
/// A copy of the last accepted reply header arriving within 100 ms of it is a duplicate and is ignored.</summary>
internal sealed class RogChannel(IHidConnection connection, byte reportId, TimeProvider time)
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(100);
    private const int Attempts = 2;
    private const int MaxReadsPerAttempt = 64;

    private byte[]? _lastHeader;
    private DateTimeOffset _lastReplyAt;

    public RogExchange Transact(RogCommand command, TimeSpan timeout)
    {
        var request = RogProtocol.BuildRequest(connection.Device.OutputReportLength, reportId, command);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            connection.FlushQueue();
            connection.Write(request);
            var deadline = time.GetUtcNow() + timeout;
            for (var reads = 0; reads < MaxReadsPerAttempt; reads++)
            {
                var remaining = deadline - time.GetUtcNow();
                if (remaining <= TimeSpan.Zero || connection.Read(remaining) is not { } report) break;

                switch (RogProtocol.Classify(report, reportId, command))
                {
                    case RogFrame.Rejected:
                        return new RogExchange(RogOutcome.Rejected, report);
                    case RogFrame.Reply when !IsDuplicate(report, command):
                        _lastHeader = report[..HeaderLength(command)];
                        _lastReplyAt = time.GetUtcNow();
                        return new RogExchange(RogOutcome.Reply, report);
                }
            }
        }
        return new RogExchange(RogOutcome.NoReply, null);
    }

    private bool IsDuplicate(byte[] report, RogCommand command) =>
        _lastHeader is not null && time.GetUtcNow() - _lastReplyAt < DuplicateWindow &&
        report.AsSpan(0, HeaderLength(command)).SequenceEqual(_lastHeader);

    // The A0 00 scan echoes only cmd and sub; everything else echoes the full 4-byte header.
    private static int HeaderLength(RogCommand command) => command == RogProtocol.ScanForPairedDevices ? 3 : RogProtocol.HeaderLength;
}
