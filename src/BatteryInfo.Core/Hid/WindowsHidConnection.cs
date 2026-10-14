using System.Runtime.InteropServices;
using BatteryInfo.Core.Diagnostics;
using BatteryInfo.Core.Interop;
using Microsoft.Win32.SafeHandles;

namespace BatteryInfo.Core.Hid;

/// <summary>An overlapped, shared handle to one HID collection.</summary>
internal sealed unsafe class WindowsHidConnection : IHidConnection
{
    private const int ErrorTimeout = 1460;
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(1);

    private readonly SafeFileHandle _handle;
    private readonly ManualResetEvent _event = new(false);
    private readonly object _gate = new();

    private WindowsHidConnection(HidDeviceInfo device, SafeFileHandle handle)
    {
        Device = device;
        _handle = handle;
    }

    public HidDeviceInfo Device { get; }

    public static WindowsHidConnection Open(HidDeviceInfo device, HidAccess access)
    {
        var desired = access == HidAccess.ReadWrite ? Kernel32.GenericRead | Kernel32.GenericWrite : 0;
        var handle = Kernel32.CreateFile(device.Path, desired, Kernel32.FileShareRead | Kernel32.FileShareWrite, IntPtr.Zero,
            Kernel32.OpenExisting, Kernel32.FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new HidIoException($"Cannot open {device.Label}", error);
        }
        return new WindowsHidConnection(device, handle);
    }

    public void Write(ReadOnlySpan<byte> report)
    {
        if (report.Length != Device.OutputReportLength)
            throw new ArgumentException($"{Device.Label} output reports are {Device.OutputReportLength} bytes, got {report.Length}.", nameof(report));

        Log.Frame(Device.Label, outgoing: true, report);
        lock (_gate)
        {
            var buffer = (byte*)NativeMemory.Alloc((nuint)report.Length);
            var overlapped = NewOverlapped();
            try
            {
                report.CopyTo(new Span<byte>(buffer, report.Length));
                if (!Kernel32.WriteFile(_handle, buffer, (uint)report.Length, null, overlapped))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != Kernel32.ErrorIoPending) throw new HidIoException($"WriteFile to {Device.Label} failed", error);
                }
                if (Complete(overlapped, WriteTimeout) is null)
                    throw new HidIoException($"WriteFile to {Device.Label} timed out", ErrorTimeout);
            }
            finally
            {
                NativeMemory.Free(buffer);
                NativeMemory.Free(overlapped);
            }
        }
    }

    public byte[]? Read(TimeSpan timeout)
    {
        var length = Device.InputReportLength;
        if (length <= 0) throw new InvalidOperationException($"{Device.Label} has no input reports.");

        lock (_gate)
        {
            var buffer = (byte*)NativeMemory.AllocZeroed((nuint)length);
            var overlapped = NewOverlapped();
            try
            {
                if (!Kernel32.ReadFile(_handle, buffer, (uint)length, null, overlapped))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != Kernel32.ErrorIoPending) throw new HidIoException($"ReadFile from {Device.Label} failed", error);
                }
                if (Complete(overlapped, timeout) is not { } read) return null;

                var report = new Span<byte>(buffer, (int)read).ToArray();
                Log.Frame(Device.Label, outgoing: false, report);
                return report;
            }
            finally
            {
                NativeMemory.Free(buffer);
                NativeMemory.Free(overlapped);
            }
        }
    }

    public void SetFeature(ReadOnlySpan<byte> report)
    {
        Log.Frame($"{Device.Label} feature", outgoing: true, report);
        lock (_gate)
        {
            fixed (byte* p = report)
            {
                if (!HidApi.HidD_SetFeature(_handle, p, (uint)report.Length))
                    throw new HidIoException($"HidD_SetFeature on {Device.Label} failed", Marshal.GetLastPInvokeError());
            }
        }
    }

    public void GetFeature(Span<byte> report)
    {
        lock (_gate)
        {
            fixed (byte* p = report)
            {
                if (!HidApi.HidD_GetFeature(_handle, p, (uint)report.Length))
                    throw new HidIoException($"HidD_GetFeature on {Device.Label} failed", Marshal.GetLastPInvokeError());
            }
        }
        Log.Frame($"{Device.Label} feature", outgoing: false, report);
    }

    public void FlushQueue()
    {
        lock (_gate) HidApi.HidD_FlushQueue(_handle);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_handle.IsClosed) Kernel32.CancelIoEx(_handle, null);
            _handle.Dispose();
            _event.Dispose();
        }
    }

    private NativeOverlapped* NewOverlapped()
    {
        var overlapped = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
        _event.Reset();
        overlapped->EventHandle = _event.SafeWaitHandle.DangerousGetHandle();
        return overlapped;
    }

    // Waits for the I/O and returns the bytes transferred, or null on timeout. On timeout the I/O is cancelled and
    // waited for, so the caller can free its buffers.
    private uint? Complete(NativeOverlapped* overlapped, TimeSpan timeout)
    {
        if (!_event.WaitOne(timeout))
        {
            Kernel32.CancelIoEx(_handle, overlapped);
            // True here means the I/O finished just before the cancel took effect.
            return Kernel32.GetOverlappedResult(_handle, overlapped, out var late, wait: true) ? late : null;
        }
        if (!Kernel32.GetOverlappedResult(_handle, overlapped, out var transferred, wait: false))
            throw new HidIoException($"I/O on {Device.Label} failed", Marshal.GetLastPInvokeError());
        return transferred;
    }
}
