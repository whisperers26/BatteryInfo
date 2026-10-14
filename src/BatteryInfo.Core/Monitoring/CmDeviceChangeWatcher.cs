using System.Runtime.InteropServices;
using BatteryInfo.Core.Diagnostics;

namespace BatteryInfo.Core.Monitoring;

/// <summary>Raises <see cref="Changed"/> when a HID collection or a Bluetooth device interface arrives or is
/// removed (CM_Register_Notification). Works without a window.</summary>
public sealed class CmDeviceChangeWatcher : IDeviceChangeSource
{
    private static readonly Guid HidInterface = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    private static readonly Guid BluetoothPortDevice = new("0850302A-B344-4FDA-9BE9-90576B8D46F0");

    private const int FilterTypeDeviceInterface = 0;
    private const int ActionInterfaceArrival = 0;
    private const int ActionInterfaceRemoval = 1;

    private readonly List<IntPtr> _registrations = [];
    private readonly NotifyCallback _callback;

    public CmDeviceChangeWatcher()
    {
        _callback = OnNotification; // kept in a field so the GC never collects it while Windows holds it
        Register(HidInterface);
        Register(BluetoothPortDevice);
    }

    public event EventHandler? Changed;

    public void Dispose()
    {
        foreach (var registration in _registrations) CM_Unregister_Notification(registration);
        _registrations.Clear();
    }

    private void Register(Guid interfaceClass)
    {
        var filter = new NotifyFilter
        {
            Size = (uint)Marshal.SizeOf<NotifyFilter>(),
            FilterType = FilterTypeDeviceInterface,
            ClassGuid = interfaceClass,
        };
        var cr = CM_Register_Notification(ref filter, IntPtr.Zero, _callback, out var registration);
        if (cr == 0) _registrations.Add(registration);
        else Log.Warn($"CM_Register_Notification for {interfaceClass} failed (CR {cr})");
    }

    private uint OnNotification(IntPtr notify, IntPtr context, int action, IntPtr eventData, uint eventDataSize)
    {
        // Runs on a system thread that must not block (and must never unregister from here).
        if (action is ActionInterfaceArrival or ActionInterfaceRemoval)
            ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke(this, EventArgs.Empty));
        return 0;
    }

    // CM_NOTIFY_FILTER: four DWORDs, then a union whose largest member is WCHAR[MAX_DEVICE_ID_LEN] (400 bytes).
    [StructLayout(LayoutKind.Sequential, Size = 416)]
    private struct NotifyFilter
    {
        public uint Size;
        public uint Flags;
        public int FilterType;
        public uint Reserved;
        public Guid ClassGuid;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint NotifyCallback(IntPtr notify, IntPtr context, int action, IntPtr eventData, uint eventDataSize);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Register_Notification(ref NotifyFilter filter, IntPtr context, NotifyCallback callback, out IntPtr notifyContext);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Unregister_Notification(IntPtr notifyContext);
}
