using System.Buffers.Binary;
using System.Text;
using BatteryInfo.Core.Tests.Fakes;

namespace BatteryInfo.Core.Tests.Vendors.Logitech;

internal enum FakeLink
{
    /// <summary>Answers HID++ 2.0.</summary>
    Online,

    /// <summary>Paired but unreachable: the receiver answers every request with 0x8F error 0x09.</summary>
    Asleep,

    /// <summary>Nothing answers.</summary>
    Silent,

    /// <summary>A HID++ 1.0 device: 2.0 requests get 0x8F error 0x01, battery registers answer.</summary>
    Hidpp10,
}

/// <summary>A simulated HID++ device with the PRO X2 SUPERSTRIKE's identity, features and battery by default.</summary>
internal sealed class FakeHidppDevice
{
    public FakeLink Link { get; set; } = FakeLink.Online;

    /// <summary>Ping still answers, but every later request goes unanswered (the device fell asleep mid-poll).</summary>
    public bool SilentAfterPing { get; set; }

    public uint UnitId { get; set; } = 0xB4655832;

    public ushort Wpid { get; set; } = 0x40BD;

    public ushort UsbPid { get; set; } = 0xC0A8;

    public byte PairingKind { get; set; } = 2;

    public string Codename { get; set; } = "X2 SUPERSTRIKE";

    public string Name { get; set; } = "PRO X2 SUPERSTRIKE";

    public byte DeviceType { get; set; } = 3;

    public (byte Major, byte Minor) Protocol { get; set; } = (4, 2);

    /// <summary>Feature id to index, as resolved on the real mouse.</summary>
    public Dictionary<ushort, byte> Features { get; } = new()
    {
        [0x0001] = 0x01, [0x0003] = 0x02, [0x0005] = 0x03, [0x1D4B] = 0x04, [0x1004] = 0x06,
    };

    public byte[] UnifiedCapabilities { get; set; } = [0x0F, 0x0F, 0x02];

    public byte[] UnifiedStatus { get; set; } = [0x38, 0x04, 0x00, 0x00];

    public byte[] BatteryStatus { get; set; } = [0x32, 0x1E, 0x00];

    public byte[] BatteryVoltage { get; set; } = [0x0F, 0xA0, 0x00];

    public byte[] AdcMeasurement { get; set; } = [0x0F, 0x3C, 0x01];

    public byte[]? Register0D { get; set; } = [0x4B, 0x00, 0x30];

    public byte[]? Register07 { get; set; } = [0x05, 0x00, 0x00];

    /// <summary>HID++ 2.0 error codes to answer the next non-IRoot feature calls with.</summary>
    public Queue<byte> NextErrors { get; } = new();

    public void MoveFeature(ushort featureId, byte index) => Features[featureId] = index;
}

/// <summary>A receiver (or a device on its own cable when <see cref="Wired"/>) behind a pair of fake HID++
/// collections. It answers what the real hardware answers, on the collection the real hardware uses.</summary>
internal sealed class FakeHidppReceiver
{
    public FakeHidppReceiver(FakeHidBackend backend, ushort productId = 0xC54D,
        string parent = @"USB\VID_046D&PID_C54D&MI_02\B&18153BD2&0&0002", bool wired = false)
    {
        Wired = wired;
        Short = backend.Add(0x046D, productId, 0xFF00, 0x0001, 7, 7, mi: 2, parent: parent);
        Long = backend.Add(0x046D, productId, 0xFF00, 0x0002, 20, 20, mi: 2, parent: parent);
        Short.OnWrite = OnWrite;
    }

    public FakeHidDevice Short { get; }

    public FakeHidDevice Long { get; }

    public bool Wired { get; }

    public uint Serial { get; set; } = 0xA408250B;

    public byte MaxDevices { get; set; } = 2;

    /// <summary>Nothing answers at all (a hung receiver).</summary>
    public bool Silent { get; set; }

    /// <summary>Paired devices by slot; a wired device sits at 0xFF.</summary>
    public Dictionary<byte, FakeHidppDevice> Slots { get; } = [];

    /// <summary>Called before each answer; returns reports to queue first (other software's traffic).</summary>
    public Func<byte[], IEnumerable<(bool OnLong, byte[] Report)>>? Noise { get; set; }

    public IEnumerable<byte[]> Requests => Short.Written;

    public int FeatureLookups => Short.Written.Count(f => f[2] == 0x00 && f[3] >> 4 == 0);

    public int CallsTo(byte deviceIndex, byte featureIndex, int function) =>
        Short.Written.Count(f => f[1] == deviceIndex && f[2] == featureIndex && f[3] >> 4 == function);

    public FakeHidppDevice Pair(byte slot, FakeHidppDevice? device = null) => Slots[slot] = device ?? new FakeHidppDevice();

    private void OnWrite(byte[] frame)
    {
        if (Noise?.Invoke(frame) is { } noise)
        {
            foreach (var (onLong, report) in noise) (onLong ? Long : Short).Enqueue(report);
        }
        if (Silent) return;

        var deviceIndex = frame[1];
        if (!Wired && deviceIndex == 0xFF)
        {
            AnswerReceiver(frame);
            return;
        }
        if (!Slots.TryGetValue(deviceIndex, out var device))
        {
            ShortError(frame, 0x08);
            return;
        }
        switch (device.Link)
        {
            case FakeLink.Silent:
                return;
            case FakeLink.Asleep:
                ShortError(frame, 0x09);
                return;
        }

        if (frame[2] == 0x81)
        {
            var value = frame[3] switch { 0x0D => device.Register0D, 0x07 => device.Register07, _ => null };
            if (device.Link != FakeLink.Hidpp10 || value is null) ShortError(frame, 0x02);
            else Short.Enqueue([0x10, deviceIndex, 0x81, frame[3], value[0], value[1], value[2]]);
            return;
        }
        if (device.Link == FakeLink.Hidpp10)
        {
            ShortError(frame, 0x01);
            return;
        }

        var isPing = frame[2] == 0x00 && frame[3] >> 4 == 1;
        if (device.SilentAfterPing && !isPing) return;
        AnswerFeature(frame, device);
    }

    private void AnswerReceiver(byte[] frame)
    {
        if (frame[2] != 0x83 || frame[3] != 0xB5)
        {
            ShortError(frame, 0x02);
            return;
        }

        var sub = frame[4];
        if (sub == 0x03)
        {
            var info = new byte[] { 0x03, 0, 0, 0, 0, 0x01, MaxDevices, 0x08, 0xFF };
            BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(1), Serial);
            LongReply(frame, info);
            return;
        }

        var slot = (byte)((sub & 0x0F) + 1);
        if (!Slots.TryGetValue(slot, out var device) || (sub >> 4) is not (2 or 3 or 4))
        {
            ShortError(frame, 0x09);
            return;
        }
        switch (sub >> 4)
        {
            case 2:
                LongReply(frame, [sub, 0x08, 0x08, (byte)(device.Wpid >> 8), (byte)device.Wpid, 0x11, 0x00, device.PairingKind, 0xB1, 0x01, 0x00, 0x00, 0x7F, 0x24]);
                break;
            case 3:
                var extended = new byte[] { sub, 0, 0, 0, 0, 0, 0, 0, 0, 0x01 };
                BinaryPrimitives.WriteUInt32BigEndian(extended.AsSpan(1), device.UnitId);
                LongReply(frame, extended);
                break;
            default:
                LongReply(frame, [sub, (byte)device.Codename.Length, .. Encoding.ASCII.GetBytes(device.Codename)]);
                break;
        }
    }

    private void AnswerFeature(byte[] frame, FakeHidppDevice device)
    {
        var function = frame[3] >> 4;
        if (frame[2] == 0x00)
        {
            if (function == 1)
            {
                LongReply(frame, [device.Protocol.Major, device.Protocol.Minor, frame[6]]);
            }
            else
            {
                var featureId = (ushort)(frame[4] << 8 | frame[5]);
                LongReply(frame, [device.Features.GetValueOrDefault(featureId), 0x00, 0x05]);
            }
            return;
        }

        if (device.NextErrors.TryDequeue(out var code))
        {
            LongError(frame, code);
            return;
        }
        var feature = device.Features.Where(f => f.Value == frame[2]).Select(f => (ushort?)f.Key).FirstOrDefault();
        byte[]? payload = (feature, function) switch
        {
            (0x0003, 0) => DeviceInfo(device),
            (0x0005, 0) => [(byte)device.Name.Length],
            (0x0005, 1) => [.. Encoding.ASCII.GetBytes(device.Name).Skip(frame[4]).Take(16)],
            (0x0005, 2) => [device.DeviceType],
            (0x1004, 0) => device.UnifiedCapabilities,
            (0x1004, 1) => device.UnifiedStatus,
            (0x1000, 0) => device.BatteryStatus,
            (0x1001, 0) => device.BatteryVoltage,
            (0x1F20, 0) => device.AdcMeasurement,
            _ => null,
        };
        if (feature is null) LongError(frame, 0x06);
        else if (payload is null) LongError(frame, 0x07);
        else LongReply(frame, payload);
    }

    private static byte[] DeviceInfo(FakeHidppDevice device)
    {
        var info = new byte[] { 0x02, 0, 0, 0, 0, 0x00, 0x0C, 0, 0, 0, 0, 0x00, 0x00, 0x01, 0x01 };
        BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(1), device.UnitId);
        BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(7), device.Wpid);
        BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(9), device.UsbPid);
        return info;
    }

    private void LongReply(byte[] request, byte[] payload)
    {
        var report = new byte[20];
        report[0] = 0x11;
        request.AsSpan(1, 3).CopyTo(report.AsSpan(1));
        payload.AsSpan(0, Math.Min(payload.Length, 16)).CopyTo(report.AsSpan(4));
        Long.Enqueue(report);
    }

    private void LongError(byte[] request, byte code)
    {
        var report = new byte[20];
        report[0] = 0x11;
        report[1] = request[1];
        report[2] = 0xFF;
        report[3] = request[2];
        report[4] = request[3];
        report[5] = code;
        Long.Enqueue(report);
    }

    private void ShortError(byte[] request, byte code) =>
        Short.Enqueue([0x10, request[1], 0x8F, request[2], request[3], code, 0x00]);
}
