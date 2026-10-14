# BatteryInfo Tray App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A .NET 10 WPF tray app that shows battery level and charging state for the user's Logitech, ROG, Razer, HUAWEI FreeClip 2 and Betop devices, with start-with-Windows, low-battery toasts and a history graph.

**Architecture:** `BatteryInfo.Core` holds all device logic behind `IBatteryProvider` (one per vendor) on top of a mockable HID layer (`IHidBackend`/`IHidConnection`) and a per-vendor command allow-list enforced at the single write choke point. `BatteryMonitor` polls providers and merges readings in `DeviceStore`; the WPF app and a console CLI consume snapshots.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.19041.0`), WPF (Fluent `ThemeMode`), WinForms `NotifyIcon`, Win32 SetupAPI/hid.dll/CfgMgr32 P/Invoke, Winsock/WinRT RFCOMM, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-14-battery-tray-app-design.md` (read it first). Protocol references: `docs/protocols/*.md`.

## Global Constraints

- TFM `net10.0-windows10.0.19041.0` for every project; `Nullable` enable; `ImplicitUsings` enable; `TreatWarningsAsErrors` true (WPF0001 for `ThemeMode` suppressed in the app only).
- No NuGet packages in `BatteryInfo.Core`, `BatteryInfo.App` or `BatteryInfo.Cli`. Test project: the `dotnet new xunit` template packages only.
- Never send a frame that is not on the vendor allow-list (spec §7). Every HID write goes through `GuardedHidConnection`; the Huawei socket path calls its policy before `Send`.
- Never open a HID interface exclusively; always `FILE_SHARE_READ | FILE_SHARE_WRITE`.
- Never write to Betop VID 0x20DD or 0x0517, never open the ROG FFC0 key-event collection, never connect RFCOMM to earbuds Windows does not report as connected.
- Providers never throw for device-level failures; they return readings with a `DeviceStatus` and `Detail`.
- Device ids: `logitech:<unitId hex>`, `rog:<model>:<serial>`, `huawei:<12 hex digits of BT address>`, `betop:<model>`, `razer:<wired PID hex4>`.
- App data root: `%LOCALAPPDATA%\BatteryInfo\` (`settings.json`, `history\yyyy-MM.jsonl`, `logs\batteryinfo-yyyyMMdd.log`).
- Default poll interval 120 s; provider poll timeout 15 s; low-battery threshold 20 %, re-arm at threshold + 5.
- Small commits, one topic each (`feat:`, `test:`, `fix:`, `docs:`, `chore:`), on branch `feature/battery-tray-app` (or a per-task worktree branch merged into it).

## Review Focus

1. A device unplugged or put to sleep mid-poll (handle becomes invalid, ERROR_DEVICE_NOT_CONNECTED 1167, ERROR_INVALID_HANDLE 6, ERROR_OPERATION_ABORTED 995) must end that poll quietly with the device Asleep/absent, never crash or hang the monitor. Pinned by a fake-backend test in every HID provider task (connection that throws `HidIoException` on `Read`).
2. Vendor software running at the same time (Synapse, BetopGame, G HUB, lamparray) puts foreign replies in the same input queue; they must be skipped, not parsed as ours. Pinned by interleaved-foreign-frame tests in Logitech, ROG, Betop and Razer tasks.
3. Betop pad asleep (dongle re-enumerates as 20DD:518D): the provider must report Asleep and perform zero writes. Pinned in the Betop task.
4. FreeClip not connected (in case / out of range): no RFCOMM connection attempt is made. Pinned in the Huawei task.
5. PC resume from sleep and Bluetooth reconnects: the monitor must re-poll promptly. Pinned in the monitor task (refresh on `RefreshRequested` and device-change events) and wired to `SystemEvents.PowerModeChanged` in the app task.

---

## File Structure

```text
BatteryInfo.slnx
Directory.Build.props                         shared TFM/nullable/warnings
src/BatteryInfo.Core/BatteryInfo.Core.csproj  AllowUnsafeBlocks
  Model/DeviceKind.cs, ChargeState.cs, DeviceStatus.cs, BatteryComponent.cs, DeviceReading.cs
  Monitoring/IBatteryProvider.cs              provider contract
  Monitoring/DeviceStore.cs                   merge readings -> DeviceState snapshot
  Monitoring/DeviceState.cs                   per-device merged view
  Monitoring/LowBatteryPolicy.cs              once-per-cycle alert rule
  Monitoring/BatteryMonitor.cs                scheduler: interval, refresh, change events, timeouts
  Monitoring/IDeviceChangeSource.cs           + CmDeviceChangeWatcher.cs (CM_Register_Notification)
  Hid/HidDeviceInfo.cs                        enumeration record
  Hid/IHidBackend.cs                          IHidBackend, IHidConnection, HidAccess, HidIoException
  Hid/WindowsHidBackend.cs                    SetupAPI enumeration + open
  Hid/WindowsHidConnection.cs                 overlapped read/write, features
  Hid/Native/HidNative.cs, SetupApiNative.cs, Kernel32Native.cs, CfgMgrNative.cs
  Safety/ICommandPolicy.cs, GuardedHidConnection.cs, UnsafeCommandException.cs
  Bluetooth/BluetoothBatteryProperty.cs       Windows' {104EA319-...} 2 battery DEVPKEY reader
  Diagnostics/Log.cs                          file log + frame tracing switch
  History/HistoryStore.cs, HistoryRecord.cs   jsonl history
  Settings/AppSettings.cs, SettingsStore.cs
  Vendors/Logitech/HidppProtocol.cs, LogitechCommandPolicy.cs, LogitechBatteryProvider.cs
  Vendors/Rog/RogProtocol.cs, RogCommandPolicy.cs, RogBatteryProvider.cs, RogModels.cs
  Vendors/Huawei/HuaweiProtocol.cs, HuaweiCommandPolicy.cs, HuaweiBatteryProvider.cs, IHuaweiTransport.cs, WindowsHuaweiTransport.cs
  Vendors/Betop/BetopProtocol.cs, BetopCommandPolicy.cs, BetopBatteryProvider.cs, BetopModels.cs
  Vendors/Razer/RazerProtocol.cs, RazerCommandPolicy.cs, RazerBatteryProvider.cs
  Providers.cs                                ProviderFactory.CreateAll()
src/BatteryInfo.Cli/                          Program.cs: `list`, `hid`, `--json`, `--trace`, `--vendor`
src/BatteryInfo.App/                          App.xaml, TrayIconHost, FlyoutWindow, ViewModels, HistoryChart, AutoStart, Notifier
tests/BatteryInfo.Core.Tests/                 one test file per unit above + Fakes/FakeHidBackend.cs
scripts/publish.ps1
```

---

### Task 1: Solution scaffold

**Files:** Create `BatteryInfo.slnx`, `Directory.Build.props`, `src/BatteryInfo.Core/BatteryInfo.Core.csproj`, `src/BatteryInfo.Cli/BatteryInfo.Cli.csproj`, `src/BatteryInfo.App/BatteryInfo.App.csproj`, `tests/BatteryInfo.Core.Tests/BatteryInfo.Core.Tests.csproj`.

**Interfaces:** Produces the four projects; Core is referenced by App, Cli and Tests; Tests has `InternalsVisibleTo`.

- [ ] **Step 1:** `dotnet new sln -n BatteryInfo`; `dotnet new classlib -o src/BatteryInfo.Core`; `dotnet new console -o src/BatteryInfo.Cli`; `dotnet new wpf -o src/BatteryInfo.App`; `dotnet new xunit -o tests/BatteryInfo.Core.Tests`; add all to the solution and add project references.
- [ ] **Step 2:** `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>
  </PropertyGroup>
</Project>
```

  Remove `TargetFramework` from the generated csproj files. Core: `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` and `<InternalsVisibleTo Include="BatteryInfo.Core.Tests" />`. App: `<OutputType>WinExe</OutputType>`, `<UseWPF>true</UseWPF>`, `<UseWindowsForms>true</UseWindowsForms>`, `<AssemblyName>BatteryInfo</AssemblyName>`, `<NoWarn>$(NoWarn);WPF0001</NoWarn>`. Cli: `<AssemblyName>batteryinfo</AssemblyName>`.
- [ ] **Step 3:** Delete template `Class1.cs` / `UnitTest1.cs`. Run `dotnet build` and `dotnet test`. Expected: build succeeds, 0 tests.
- [ ] **Step 4:** Commit `chore: scaffold solution and projects`.

### Task 2: Model and provider contract

**Files:** Create `src/BatteryInfo.Core/Model/*.cs`, `Monitoring/IBatteryProvider.cs`. Test: `tests/BatteryInfo.Core.Tests/Model/DeviceReadingTests.cs`.

**Interfaces (produces, exact):**

```csharp
namespace BatteryInfo.Core.Model;
public enum DeviceKind { Mouse, Keyboard, Earbuds, Headset, Controller, Other }
public enum ChargeState { Unknown, Discharging, Charging, Full, NotCharging, Fault }
public enum DeviceStatus { Online, Asleep, Offline, NeedsAccess, Error }
public sealed record BatteryComponent(string Name, int? Percent, ChargeState Charge)
{
    public static BatteryComponent Single(int? percent, ChargeState charge) => new("", percent, charge);
}
public sealed record DeviceReading(
    string DeviceId, string Vendor, string Name, DeviceKind Kind, DeviceStatus Status,
    IReadOnlyList<BatteryComponent> Components, DateTimeOffset Timestamp, string Source,
    bool Approximate = false, string? Detail = null)
{
    public bool HasBattery => Components.Any(c => c.Percent is not null);
    public int? LowestPercent => Components.Where(c => c.Percent is not null).Select(c => c.Percent).Min();
}

namespace BatteryInfo.Core.Monitoring;
public interface IBatteryProvider : IDisposable
{
    string Vendor { get; }
    Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken);
    event EventHandler? RefreshRequested;
}
```

- [ ] **Step 1: failing test** `LowestPercent_ignores_unknown_components`: reading with components `("Left",80)`, `("Right",null)`, `("Case",100)` → `LowestPercent == 80`, `HasBattery == true`; empty components → `HasBattery == false`, `LowestPercent == null`.
- [ ] **Step 2:** run `dotnet test --filter DeviceReadingTests` → fails to compile.
- [ ] **Step 3:** add the types above. **Step 4:** tests pass. **Step 5:** commit `feat(core): add device reading model and provider contract`.

### Task 3: DeviceStore

**Files:** Create `Monitoring/DeviceState.cs`, `Monitoring/DeviceStore.cs`. Test `Monitoring/DeviceStoreTests.cs`.

**Interfaces:**

```csharp
public sealed record DeviceState(DeviceReading Latest, DeviceReading? LastWithBattery)
{
    // Components to display: latest when it has a battery, else the last known (stale) ones.
    public IReadOnlyList<BatteryComponent> DisplayComponents => Latest.HasBattery ? Latest.Components : LastWithBattery?.Components ?? [];
    public bool IsStale => !Latest.HasBattery && LastWithBattery is not null;
    public DateTimeOffset? BatteryTimestamp => Latest.HasBattery ? Latest.Timestamp : LastWithBattery?.Timestamp;
}
public sealed class DeviceStore
{
    public DeviceStore(TimeProvider time, TimeSpan retention); // retention default 7 days
    public void Seed(DeviceReading reading);                     // from history at start-up, status forced Offline
    public void Apply(string vendor, IReadOnlyList<DeviceReading> readings); // a full poll result for one vendor
    public void MarkVendorFailed(string vendor, string detail);  // provider threw: keep states, set Detail
    public IReadOnlyList<DeviceState> Snapshot();                // ordered by Vendor, then Name
}
```

Rules (each one a test): (a) Apply stores latest; (b) a reading without components keeps `LastWithBattery` from before (`IsStale`); (c) a device of the same vendor missing from `Apply` becomes `Status = Offline` with `Timestamp` unchanged, `Detail` null; (d) devices of other vendors are untouched by `Apply`; (e) `Snapshot` drops states whose `Latest.Timestamp` and battery timestamp are older than retention (use `FakeTimeProvider`-style manual `TimeProvider` subclass in tests, no NuGet); (f) `MarkVendorFailed` keeps every state of that vendor and sets `Detail`; (g) thread safety: all methods lock one private object.

- [ ] Steps: write tests (a)-(f) → run (fail) → implement → run (pass) → commit `feat(core): add device store that merges polls`.

### Task 4: LowBatteryPolicy

**Files:** `Monitoring/LowBatteryPolicy.cs`, test `Monitoring/LowBatteryPolicyTests.cs`.

```csharp
public sealed record LowBatteryAlert(string DeviceId, string DeviceName, string Component, int Percent);
public sealed class LowBatteryPolicy
{
    public int Threshold { get; set; } = 20;   // re-arm above Threshold + 5
    public IReadOnlyList<LowBatteryAlert> Evaluate(IReadOnlyList<DeviceState> states);
}
```

Tests: fires once at 20 % discharging; does not fire again at 19 %; re-arms at 26 % then fires at 20 %; does not fire while `Charging`/`Full`/`NotCharging`; does not fire for stale/Offline states; earbuds components tracked independently (key = deviceId + component name); `Unknown` charge state still fires.

- [ ] Steps: tests → fail → implement → pass → commit `feat(core): add low-battery alert policy`.

### Task 5: Diagnostics log

**Files:** `Diagnostics/Log.cs`, test `Diagnostics/LogTests.cs`.

```csharp
public static class Log
{
    public static void Configure(string? directory, bool traceFrames); // null directory = no file output
    public static bool TraceFrames { get; }
    public static void Info(string message); public static void Warn(string message);
    public static void Error(string message, Exception? ex = null);
    public static void Frame(string channel, bool outgoing, ReadOnlySpan<byte> data); // only when TraceFrames
    public static event Action<string>? LineWritten;  // for the CLI --trace and tests
}
```

Line format: `2026-10-14T12:00:00.000+08:00 INFO message` / `FRAME logitech >> 10 01 06 1C 00 00 00`. File `batteryinfo-yyyyMMdd.log`; on Configure delete `batteryinfo-*.log` older than 7 days. Writes are serialized by a lock and never throw (I/O errors swallowed).

Tests: `Frame` emits nothing when tracing off; emits uppercase spaced hex when on; file is written to a temp dir.

- [ ] Steps: tests → fail → implement → pass → commit `feat(core): add file log with frame tracing`.

### Task 6: HID abstraction and Windows backend

**Files:** `Hid/HidDeviceInfo.cs`, `Hid/IHidBackend.cs`, `Hid/WindowsHidBackend.cs`, `Hid/WindowsHidConnection.cs`, `Hid/Native/*.cs`; tests `Hid/HidDeviceInfoTests.cs`; `tests/.../Fakes/FakeHidBackend.cs`.

**Interfaces (produces, exact):**

```csharp
public sealed record HidDeviceInfo(
    string Path, ushort VendorId, ushort ProductId, ushort Version,
    ushort UsagePage, ushort Usage,
    int InputReportLength, int OutputReportLength, int FeatureReportLength,
    int? InterfaceNumber,          // from "&mi_XX" in the path
    string? ParentInstanceId,      // USB interface devnode, e.g. USB\VID_046D&PID_C54D&MI_02\B&18153BD2&0&0002
    string? Product, string? Serial,
    byte? OutputReportId,          // first output report id from HidP caps, null if none
    bool Accessible)               // zero-access open + caps succeeded
{
    public static int? ParseInterfaceNumber(string path);
}
public enum HidAccess { None, ReadWrite }
public sealed class HidIoException(string message, int win32Error) : IOException(message) { public int Win32Error { get; } = win32Error;
    public bool DeviceGone => Win32Error is 1167 or 6 or 995 or 31 or 22; }
public interface IHidConnection : IDisposable
{
    HidDeviceInfo Device { get; }
    void Write(ReadOnlySpan<byte> report);          // exactly OutputReportLength bytes, WriteFile
    byte[]? Read(TimeSpan timeout);                 // one input report (InputReportLength) or null on timeout
    void SetFeature(ReadOnlySpan<byte> report);     // HidD_SetFeature
    void GetFeature(Span<byte> report);             // HidD_GetFeature, report[0] = report id on entry
    void FlushQueue();                              // HidD_FlushQueue
}
public interface IHidBackend
{
    IReadOnlyList<HidDeviceInfo> Enumerate();
    IHidConnection Open(HidDeviceInfo device, HidAccess access); // throws HidIoException (5 = access denied)
}
```

Implementation notes:
- Enumerate: `SetupDiGetClassDevsW(HidD_GetHidGuid, null, 0, DIGCF_PRESENT|DIGCF_DEVICEINTERFACE)`, loop `SetupDiEnumDeviceInterfaces` + `SetupDiGetDeviceInterfaceDetailW` (get path and `SP_DEVINFO_DATA.DevInst`). Parent: `CM_Get_Parent(out parent, devInst)` then `CM_Get_Device_IDW`. For each path `CreateFileW(path, 0, FILE_SHARE_READ|FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0)`; on success `HidD_GetAttributes`, `HidD_GetPreparsedData`, `HidP_GetCaps`, output report id from `HidP_GetValueCaps(HidP_Output)` or `HidP_GetButtonCaps(HidP_Output)` first entry, `HidD_GetProductString`, `HidD_GetSerialNumberString` (ignore failures). On open failure: `Accessible=false`, VID/PID parsed from `vid_xxxx&pid_xxxx` in the path, lengths 0.
- Connection: `CreateFileW(path, access==ReadWrite ? GENERIC_READ|GENERIC_WRITE : 0, share RW, OPEN_EXISTING, FILE_FLAG_OVERLAPPED)`. `Write`: validate length == OutputReportLength (else `ArgumentException`), overlapped `WriteFile` + `GetOverlappedResult(wait: true)` with a 1 s `WaitForSingleObject` guard and `CancelIoEx` on timeout. `Read`: native buffer + `NativeOverlapped` allocated with `NativeMemory`, manual-reset event, `ReadFile`; `ERROR_IO_PENDING` → `WaitForSingleObject(timeout)`; timeout → `CancelIoEx` + `GetOverlappedResult(wait: true)` → return null. Failures → `HidIoException(win32Error)`. `Log.Frame` on every write/read/feature.
- `HidDeviceInfoTests`: `ParseInterfaceNumber(@"\\?\hid#vid_046d&pid_c54d&mi_02&col01#c&1823a09c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}") == 2`; no `&mi_` → null; uppercase `&MI_03` → 3.
- `FakeHidBackend` (tests): holds a list of `FakeHidDevice { Info; Func<byte[], IEnumerable<byte[]>> OnWrite; Func<byte[], byte[]>? OnFeature; Queue<byte[]> Pending; List<byte[]> Written; Exception? ThrowOnRead }`. `Read` dequeues or returns null; `Write` records and enqueues the `OnWrite` responses.
- Hardware smoke check: Cli `batteryinfo hid` prints one line per interface: `vid:pid mi up:u in/out/feat rid acc path`. Run it and confirm 046D:C54D MI_02 FF00/1 7/7 and FF00/2 20/20, 0B05:1ACE MI_02 FF02/FF00/FF01 64/64.

- [ ] Steps: interface-number tests → fail → implement record → pass → commit `feat(core): add HID device info and backend contract`; implement natives/backend/connection → `dotnet build` → add Cli `hid` command → run on hardware → commit `feat(core): add Windows HID backend` and `feat(cli): add hid listing command`.

### Task 7: Command safety guard

**Files:** `Safety/ICommandPolicy.cs`, `Safety/UnsafeCommandException.cs`, `Safety/GuardedHidConnection.cs`; test `Safety/GuardedHidConnectionTests.cs`.

```csharp
public interface ICommandPolicy { bool IsAllowed(HidDeviceInfo device, ReadOnlySpan<byte> outgoing); }
public sealed class UnsafeCommandException(string message) : InvalidOperationException(message);
public sealed class GuardedHidConnection(IHidConnection inner, ICommandPolicy policy) : IHidConnection
// Write and SetFeature call policy.IsAllowed first; refused -> Log.Warn + throw UnsafeCommandException, nothing sent.
// GetFeature passes through only when policy allows the bytes in the buffer (report id + any request bytes).
```

Tests: allowed frame reaches the inner fake; refused frame throws and the fake records zero writes; `SetFeature` guarded likewise.

- [ ] Steps: tests → fail → implement → pass → commit `feat(core): guard every HID write with a command policy`.

### Task 8: Bluetooth battery property reader

**Files:** `Bluetooth/BluetoothBatteryProperty.cs`; manual check via Cli.

```csharp
public static class BluetoothBatteryProperty
{
    // Reads DEVPKEY {104EA319-6EE2-4701-BD47-8DDBF425BBE5} pid 2 (DEVPROP_TYPE_BYTE) from every present
    // devnode whose instance id ends with "_<address hex>_C00000000" or contains the address; returns the first value.
    public static int? Read(ulong bluetoothAddress);
}
```

Use `CM_Get_Device_ID_ListW` (filter `CM_GETIDLIST_FILTER_PRESENT`) over enumerator `BTHENUM`, match the 12-hex address case-insensitively, `CM_Locate_DevNodeW` + `CM_Get_DevNode_PropertyW`. Manual check: `batteryinfo bt-battery C0DA5EA83EC7` prints a value 0-100 (Windows showed 80).

- [ ] Steps: implement → Cli subcommand → run → commit `feat(core): read Windows Bluetooth battery property`.

### Task 9: BatteryMonitor and device-change watcher

**Files:** `Monitoring/IDeviceChangeSource.cs`, `Monitoring/CmDeviceChangeWatcher.cs`, `Monitoring/BatteryMonitor.cs`; test `Monitoring/BatteryMonitorTests.cs`.

```csharp
public interface IDeviceChangeSource : IDisposable { event EventHandler? Changed; }
public sealed class BatteryMonitor : IAsyncDisposable
{
    public BatteryMonitor(IReadOnlyList<IBatteryProvider> providers, DeviceStore store, IDeviceChangeSource? changes,
                          TimeProvider time, TimeSpan pollInterval, TimeSpan providerTimeout);
    public TimeSpan PollInterval { get; set; }
    public DateTimeOffset? LastRefresh { get; }
    public event EventHandler<IReadOnlyList<DeviceState>>? SnapshotChanged;
    public void Start();                       // immediate poll, then timer
    public Task RefreshAsync(CancellationToken ct = default); // poll all providers now (coalesces concurrent calls)
}
```

Behaviour/tests (fake providers + manual TimeProvider): polls every provider on Start; a provider that throws → `store.MarkVendorFailed`, others applied; a provider exceeding the timeout is cancelled and marked failed; `RefreshRequested` triggers a poll (debounced 2 s); `Changed` triggers a poll after 2 s debounce, and 3 events within 2 s cause one poll; `SnapshotChanged` raised after each poll; overlapping `RefreshAsync` calls share one in-flight poll. `CmDeviceChangeWatcher` registers `CM_Register_Notification` for the HID interface class `{4D1E55B2-F16F-11CF-88CB-001111000030}` and `GUID_BTHPORT_DEVICE_INTERFACE {0850302A-B344-4FDA-9BE9-90576B8D46F0}` arrival/removal and raises `Changed` (callback must not block: just raise).

- [ ] Steps: tests → fail → implement monitor → pass → commit `feat(core): add battery monitor scheduler`; implement watcher → build → commit `feat(core): watch HID and Bluetooth device changes`.

### Tasks 10-14: Vendor providers (independent, parallel)

Each provider task:
- Files: `Vendors/<V>/<V>Protocol.cs` (pure, static builders/parsers), `<V>CommandPolicy.cs`, `<V>BatteryProvider.cs`, plus tests `tests/.../Vendors/<V>/<V>ProtocolTests.cs`, `<V>CommandPolicyTests.cs`, `<V>BatteryProviderTests.cs` using `FakeHidBackend`.
- Consumes: Tasks 2, 5, 6, 7 (and 8 for Huawei/Razer BLE fallback).
- Produces: `public sealed class <V>BatteryProvider : IBatteryProvider` with constructor `(IHidBackend hid)` (Huawei: `(IHuaweiTransport transport)`), `Vendor` = "Logitech"/"ROG"/"HUAWEI"/"Betop"/"Razer".
- TDD order: protocol tests with the exact captured vectors from `docs/protocols/<v>.md` → policy tests (allowed list + dangerous neighbours) → provider tests with the fake backend (online, asleep, foreign frames interleaved, device gone mid-read, access denied) → implementation → commit per layer: `feat(<v>): add protocol codec`, `feat(<v>): add command allow-list`, `feat(<v>): add battery provider`.
- Hardware check at the end: `dotnet run --project src/BatteryInfo.Cli -- list --vendor <v> --trace` shows the live reading (where the device is present).

Required vectors and cases per vendor (from the protocol docs; tests must use these bytes):

**Task 10 Logitech** (`docs/protocols/logitech.md`): ping reply `11 01 00 1E 04 02 A2` → HID++ 4.2 online; getFeature(0x1004) reply index 0x06 v5; getStatus reply `11 01 06 1E 38 04 00 00 00 00 ...` → 56 %, level GOOD, Discharging; charging byte mapping 1/2/5 → Charging, 3 → Full, 4 → Fault, 0 → Discharging, ≥6 → Unknown; empty slot `10 FF 8F 83 B5 09 00` → skip; ping error `10 01 8F 00 1E 09 00` → Asleep; name loop 'PRO X2 SUPERSTRIKE'; receiver info `11 FF 83 B5 03 A4 08 25 0B 01 02 08 FF ...` → max devices 2; pairing `11 FF 83 B5 20 08 08 40 BD 11 00 02 B1 ...` → wpid 40BD kind 2; foreign swId replies ignored; reply arrives on the long handle for a short request; 0x1001 voltage table and 0x1F20 table lookups (4186 mV → 100, 3537 → 1, below → 0). Policy refuses `10 FF 80 00 ..`, `10 FF 80 02 02 00 00`, `0x82`, 0x1004 fn2. swId 0x0C.

**Task 11 ROG** (`docs/protocols/rog.md`): scan reply `01 A0 00 01 00 25 1C 02 04` (rid 1) → one slot PID 0x1C25 control rid 2 notify rid 4; keyboard `12 03` reply d[4]=1 → RF alive; `12 01` reply with d[5]=0x2D, d[8]=0 → 45 %, Discharging; d[8]=1 → Charging; reject `02 FF AA` → Asleep; `d[4]=0xFF` → Asleep; mouse `12 07` with d[4]=0 && d[9]=0 → Asleep (not 0 %); Cetra `[CC] 12 12` d[4..6] → Left/Right/Case; duplicate header within 100 ms ignored; writes are full 64-byte `WriteFile` buffers; FFC0/FFC1/FFC2 collections never opened. Policy refuses `A2 00`, `A2 01`, `B0 00`, any `0x50/0x51` and any non-zero payload.

**Task 12 HUAWEI** (`docs/protocols/huawei.md`): CRC16-XMODEM vectors `5A0009000108010002000300`→`FBB9`, `5A0009000108010140030100`→`ED2E`, `5A0014000108010140020310203003030001000402140A`→`1461`, `5A00050001080100`→`F53D`; request bytes exactly `5A 00 09 00 01 08 01 00 02 00 03 00 FB B9`; response `5A 00 1B 00 01 08 01 01 4B 02 03 4C 4B 64 03 03 00 00 00 04 02 14 0A 05 02 00 00 06 01 0A AB 24` → L 76 R 75 Case 100, no charging; stream resync across preamble frames `5A00030001063EBD`, the 44-byte `2BAC` frame and garbage bytes; T2 missing → single component from T1; values > 100 → null. `IHuaweiTransport` (`IsConnected(ulong)`, `IEnumerable<HuaweiDevice> FindPaired()`, `Stream OpenSpp(ulong, TimeSpan)`, `int? ReadWindowsBattery(ulong)`) with `WindowsHuaweiTransport` (Winsock AF_BTH socket, port 0 + SerialPort UUID, `BluetoothGetDeviceInfo` connected check, BT name match "HUAWEI Free*", BT VID 0x027D) and `BluetoothBatteryProperty` fallback. Provider tests: not connected → zero `OpenSpp` calls, reading Offline-free (no reading or Asleep with HFP value if present); open throws (channel busy) → HFP value, `Approximate = true`, Source "HFP (approx.)"; timeout → HFP fallback. Raises `RefreshRequested` on `BluetoothDevice.ConnectionStatusChanged` (WinRT) in the Windows transport. Policy allows only the battery request frame(s).

**Task 13 Betop** (`docs/protocols/betop.md`): request `02 15` + 62 zero bytes; reply `02 15 5B 00 50 01 00 50 01 02 00 FF FF 0B E8 E7 00 ...` → 91 % and, since `(0x50 & 0x0F) == 0`, Discharging; `byte4 = 0x51` → Charging; values > 100 clamp to 100; model table (KP70C2 518C/518D and the SetHidWriteEnc 0 siblings) → names; only 20DD:518D present → one reading `betop:KP70C2` Asleep, zero writes, never opened for write; foreign `02 25`/`02 11` frames skipped; `HidIoException(1167)` mid-read → Asleep, no crash. Policy: only `02 15`/`02 55`/`02 18` with zero payload on VID 0x20BC; refuses `02 14 ...`, `02 11`, `02 21`, `02 31`, VID 0x20DD and 0x0517.

**Task 14 Razer** (`docs/protocols/razer.md`): battery request (91 bytes) `00 | 00 1F 00 00 00 02 07 80 | 80×00 | 85 00`; charging request CRC `81`; `00/BF` CRC `EF`; reply raw 245 → 96 %, 255 → 100, 217 → 85 (integer `raw*100/255`); status 0x01 then 0x02 → parsed after polling; status 0x04 ×3 → Asleep; foreign txid/class reply skipped; bad CRC on 0x02 → retry; charging enum 1/4 → Charging, 5/6 → NotCharging, 2/3 → Fault, 0 → Discharging; `00/BF` reply args `02 | 01 00 D7 | 00 FF FF` → count 2, slot 1 linked to PID 00D7 (then send 07/84, 07/80); args `02 | 00 00 D7 | 00 FF FF` → not linked → Asleep without sending 07/xx; open access denied (HidIoException 5) → NeedsAccess with Detail; MI_00 selection by caps (UP 1, U 2, feature 91) excluding paths with `&col`. Policy: only 07/80, 07/84 (size 2, arg0 0) and 00/BF (size 0x50, 00D7 only); refuses any id with bit 7 clear.

### Task 15: Provider factory and CLI readings

**Files:** `src/BatteryInfo.Core/Providers.cs`, `src/BatteryInfo.Cli/Program.cs`.

```csharp
public static class ProviderFactory { public static IReadOnlyList<IBatteryProvider> CreateAll(); } // Windows backends
```

CLI: `batteryinfo list [--json] [--vendor <name>] [--trace]` polls once (each provider with a 15 s timeout) and prints `Vendor  Name  Status  Components  Source  Detail`; `hid`; `bt-battery <addr>`. Hardware check: run `list` and record the output in the PR description.

- [ ] Steps: implement → run on hardware → commit `feat(cli): print live battery readings`.

### Task 16: Settings and history stores

**Files:** `Settings/AppSettings.cs`, `Settings/SettingsStore.cs`, `History/HistoryRecord.cs`, `History/HistoryStore.cs`; tests for both.

```csharp
public sealed record AppSettings(int PollIntervalSeconds = 120, int LowBatteryThreshold = 20, bool NotificationsEnabled = true, bool TraceFrames = false);
public sealed class SettingsStore(string path) { public AppSettings Load(); public void Save(AppSettings s); } // corrupt/missing -> defaults
public sealed record HistoryRecord(DateTimeOffset Time, string DeviceId, string DeviceName, string Vendor, DeviceKind Kind, string Component, int? Percent, ChargeState Charge, DeviceStatus Status);
public sealed class HistoryStore(string directory, TimeProvider time)
{
    public void Append(IReadOnlyList<DeviceState> snapshot);  // writes only on change or every 30 min per (device, component)
    public IReadOnlyList<HistoryRecord> Query(string deviceId, DateTimeOffset since);
    public IReadOnlyList<DeviceReading> LastKnownReadings();  // newest record per device -> DeviceReading (for DeviceStore.Seed)
    public void Prune(TimeSpan keep);                          // delete month files older than keep (90 days)
}
```

Tests: round-trip; unchanged values written once per 30 min; corrupt line skipped; prune deletes old month files; settings defaults on missing/corrupt file.

- [ ] Steps: tests → fail → implement → pass → commit `feat(core): add settings store` and `feat(core): add battery history store`.

### Task 17: Tray app shell (tray icon, flyout, view models)

**Files:** `src/BatteryInfo.App/App.xaml(.cs)`, `Tray/TrayIconHost.cs`, `Tray/TrayIconRenderer.cs`, `Flyout/FlyoutWindow.xaml(.cs)`, `Flyout/FlyoutPlacement.cs`, `ViewModels/ObservableObject.cs`, `ViewModels/FlyoutViewModel.cs`, `ViewModels/DeviceViewModel.cs`, `ViewModels/ComponentViewModel.cs`.

Behaviour: single-instance mutex `Local\BatteryInfo.SingleInstance`; `ThemeMode="System"`; no main window; `ShutdownMode=OnExplicitShutdown`. Tray icon: Segoe Fluent Icons battery glyph (U+E83F) rendered to a 32×32 bitmap (white on dark taskbar, black on light; follow `AppsUseLightTheme`/`SystemUsesLightTheme`), tooltip summary ≤ 127 chars, left click toggles flyout, context menu Refresh / Start with Windows / Exit. Flyout: `WindowStyle=None`, `ResizeMode=NoResize`, `Topmost`, `ShowInTaskbar=False`, width 360, rounded corners via `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE=33, DWMWCP_ROUND=2)`, placed by `FlyoutPlacement` (taskbar edge from `SHAppBarMessage(ABM_GETTASKBARPOS)`, 12 px margin, DPI aware), hides on `Deactivated`. Rows per spec §9 with kind glyphs (mouse E962, keyboard E765, earbuds/headset E7F6, controller E7FC). Status text: Online → charge text; Asleep → "Asleep · 82 % 14 min ago"; Offline → "Not connected · last 82 % yesterday"; NeedsAccess/Error → Detail. Approximate → "≈". Opening the flyout refreshes when `LastRefresh` older than 30 s. `SystemEvents.PowerModeChanged` Resume → `RefreshAsync` after 5 s.

- [ ] Steps: build + run the app → screenshot the flyout → commit `feat(app): add tray icon and device flyout`.

### Task 18: Notifications, autostart, settings UI

**Files:** `Services/AutoStart.cs`, `Services/LowBatteryNotifier.cs`, flyout footer bindings.

AutoStart: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `BatteryInfo` = `"<Environment.ProcessPath>" --tray`; `IsEnabled` reads the value and compares the path. Notifier: on each snapshot, `LowBatteryPolicy.Evaluate` → `NotifyIcon.ShowBalloonTip(5000, "Low battery", "PRO X2 SUPERSTRIKE is at 18 %", ToolTipIcon.Warning)` when `NotificationsEnabled`. Footer: threshold ComboBox (10/15/20/25/30), notifications toggle, start-with-Windows toggle, refresh button, "Updated hh:mm".

- [ ] Steps: implement → run → verify registry value and a forced test toast (temporarily threshold 100 via settings) → commit `feat(app): add low-battery toasts` and `feat(app): add start with Windows toggle`.

### Task 19: History chart

**Files:** `Controls/HistoryChart.cs` (custom `FrameworkElement` drawing a polyline per component over 7 days, 0-100 % axis with 25 % gridlines, charging segments tinted), wiring in `DeviceViewModel.IsExpanded`.

- [ ] Steps: implement → run with real history → screenshot → commit `feat(app): add 7-day battery history chart`.

### Task 20: Publish script and README

**Files:** `scripts/publish.ps1` (framework-dependent `win-x64` publish of the app to `$env:LOCALAPPDATA\Programs\BatteryInfo`, stops a running instance first), `README.md` (devices, build, run, publish, CLI, safety note, protocol docs links).

- [ ] Steps: write → run publish → start the published exe → commit `chore: add publish script` and `docs: expand README`.

### Task 21: Review, hardware verification, push

- [ ] Whole-branch review (safety allow-lists, error handling, threading, UI).
- [ ] `dotnet test` all green; `batteryinfo list` live output captured for Logitech, ROG, HUAWEI, Betop.
- [ ] `gh repo create whisperers26/BatteryInfo --private`, push `main` and `feature/battery-tray-app`, open a PR.
