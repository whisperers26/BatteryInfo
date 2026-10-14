using BatteryInfo.Core.Tests.Fakes;
using BatteryInfo.Core.Vendors.Rog;

namespace BatteryInfo.Core.Tests.Vendors.Rog;

/// <summary>Answers ROG requests written to a fake collection: each request header maps to the reports queued in
/// reply. A request with no entry gets no reply (a timeout).</summary>
internal sealed class RogResponder
{
    private readonly Dictionary<RogCommand, Queue<byte[][]>> _answers = [];

    public RogResponder(FakeHidDevice device)
    {
        Device = device;
        device.OnWrite = OnWrite;
    }

    public FakeHidDevice Device { get; }

    /// <summary>Every later request for <paramref name="command"/> is answered with <paramref name="reports"/>.</summary>
    public RogResponder On(RogCommand command, params byte[][] reports)
    {
        _answers[command] = new Queue<byte[][]>([reports]);
        return this;
    }

    /// <summary>Successive requests for <paramref name="command"/> get successive answers; the last one repeats.</summary>
    public RogResponder OnSequence(RogCommand command, params byte[][][] answers)
    {
        _answers[command] = new Queue<byte[][]>(answers);
        return this;
    }

    public RogResponder Silent(RogCommand command)
    {
        _answers.Remove(command);
        return this;
    }

    public IEnumerable<RogCommand> Commands => Device.Written.Select(Header);

    public static RogCommand Header(byte[] frame) => new(frame[1], frame[2], (ushort)(frame[3] | frame[4] << 8));

    private void OnWrite(byte[] frame)
    {
        if (!_answers.TryGetValue(Header(frame), out var queue)) return;
        var reports = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        foreach (var report in reports) Device.Enqueue(report);
    }
}

/// <summary>The ROG OMNI receiver 0B05:1ACE as it enumerates on this PC (rog.md §2), with an Azoth X paired in the
/// keyboard slot and an empty mouse slot.</summary>
internal sealed class FakeOmni
{
    public const string Serial = "T4MPKRD01THC";

    public FakeOmni(FakeHidBackend backend, string? serial = Serial, ushort pid = 0x1ACE)
    {
        var mi02 = $@"USB\VID_0B05&PID_{pid:X4}&MI_02\B&26A9A3E1&0&0002";
        var mi03 = $@"USB\VID_0B05&PID_{pid:X4}&MI_03\B&26A9A3E1&0&0003";
        const string product = "ROG OMNI RECEIVER";

        BootKeyboard = backend.Add(0x0B05, pid, 0x0001, 0x0006, 9, 2, mi: 0, product: product, serial: serial, outputReportId: 1);
        Receiver = new RogResponder(backend.Add(0x0B05, pid, 0xFF02, 1, 64, 64, mi: 2, parent: mi02, product: product, serial: serial, outputReportId: 1));
        KeyboardSlot = new RogResponder(backend.Add(0x0B05, pid, 0xFF00, 1, 64, 64, mi: 2, parent: mi02, product: product, serial: serial, outputReportId: 2));
        MouseSlot = new RogResponder(backend.Add(0x0B05, pid, 0xFF01, 1, 64, 64, mi: 2, parent: mi02, product: product, serial: serial, outputReportId: 3));
        DongleEvents = backend.Add(0x0B05, pid, 0xFFC2, 1, 21, 0, mi: 3, parent: mi03, product: product, serial: serial);
        KeyboardEvents = backend.Add(0x0B05, pid, 0xFFC0, 1, 21, 0, mi: 3, parent: mi03, product: product, serial: serial);
        MouseEvents = backend.Add(0x0B05, pid, 0xFFC1, 1, 21, 0, mi: 3, parent: mi03, product: product, serial: serial);

        Receiver.On(RogProtocol.ScanForPairedDevices, ScanAzothX)
            .On(RogProtocol.ReceiverSerialNumber, SerialReply);
        KeyboardSlot.On(RogProtocol.KeyboardConnectionType, ConnectionRf)
            .On(RogProtocol.KeyboardPowerInfo, KeyboardPower45);
        MouseSlot.On(RogProtocol.MouseExistInfo, R(0x03, 0x12, 0x00, 0x02, 0x00, 0x00))
            .On(RogProtocol.MousePowerInfo, R(0x03, 0xFF, 0xAA));
    }

    public FakeHidDevice BootKeyboard { get; }
    public RogResponder Receiver { get; }
    public RogResponder KeyboardSlot { get; }
    public RogResponder MouseSlot { get; }
    public FakeHidDevice DongleEvents { get; }
    public FakeHidDevice KeyboardEvents { get; }
    public FakeHidDevice MouseEvents { get; }

    // Captured on this PC: one slot, PID 0x1C25, control rid 2, notify rid 4.
    public static readonly byte[] ScanAzothX = R(0x01, 0xA0, 0x00, 0x01, 0x00, 0x25, 0x1C, 0x02, 0x04);

    public static readonly byte[] SerialReply =
        R(0x01, 0xA1, 0x00, 0x00, 0x00, 0x54, 0x34, 0x4D, 0x50, 0x4B, 0x52, 0x44, 0x30, 0x31, 0x54, 0x48, 0x43);

    public static readonly byte[] ConnectionRf = R(0x02, 0x12, 0x03, 0x00, 0x00, 0x01);

    public static readonly byte[] KeyboardPower45 = KeyboardPower(0x02, 45, charging: 0);

    public static byte[] KeyboardPower(byte reportId, byte percent, byte charging) =>
        R(reportId, 0x12, 0x01, 0x00, 0x00, 0x00, percent, 0x03, 0x00, charging, 0x14, percent, 0xCA, 0x0E);

    public static byte[] MousePower(byte reportId, byte percent, byte charging) =>
        R(reportId, 0x12, 0x07, 0x00, 0x00, percent, 0x02, 0x0A, 0xD0, 0x0E, charging, 0x00);

    /// <summary>Pads a report to 64 bytes, as ReadFile returns it on the OMNI.</summary>
    public static byte[] R(params byte[] bytes)
    {
        var report = new byte[64];
        bytes.CopyTo(report, 0);
        return report;
    }

    /// <summary>Pads a report to 65 bytes, as ReadFile returns it on a report-id-0 collection.</summary>
    public static byte[] R65(params byte[] bytes)
    {
        var report = new byte[65];
        bytes.CopyTo(report, 0);
        return report;
    }
}
