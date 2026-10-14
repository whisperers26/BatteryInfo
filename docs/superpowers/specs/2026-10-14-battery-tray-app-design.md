# BatteryInfo tray app: design

Date: 2026-10-14. Status: approved approach (user chose the recommended option and asked
not to be consulted again, so the remaining decisions below were made on their behalf).

## 1. Goal

One place on a Windows 11 PC that shows the battery level, and where available the charging
state, of every connected peripheral the user owns:

| Device | Link | Protocol reference |
|---|---|---|
| Logitech PRO X2 SUPERSTRIKE mouse | LIGHTSPEED receiver 046D:C54D | [logitech.md](../../protocols/logitech.md) |
| ROG Azoth X keyboard | ROG OMNI receiver 0B05:1ACE (also USB 0B05:1C24) | [rog.md](../../protocols/rog.md) |
| ROG Cetra Open Wireless earbuds | own dongle 0B05:1CAC (not currently plugged in) | [rog.md](../../protocols/rog.md) §8 |
| HUAWEI FreeClip 2 earbuds | classic Bluetooth, SPP on RFCOMM | [huawei.md](../../protocols/huawei.md) |
| Betop KP70C2 controller | dongle 20BC:518D (20DD:518D while the pad sleeps) | [betop.md](../../protocols/betop.md) |
| Razer Basilisk V3 Pro 35K | HyperSpeed dongle 1532:00D7, cable 1532:00D6 | [razer.md](../../protocols/razer.md) |

Personal use only, single user, single PC. Success: every device above shows a correct
percentage within a couple of minutes of it changing, FreeClip shows left/right/case, and
nothing the app does changes any device setting.

## 2. What the user asked for

- A tray icon with a Windows 11 style flyout listing every device (chosen over per-device tray
  icons, a normal window, or a docked panel).
- Start with Windows.
- Low-battery toast notifications.
- A battery history graph.
- A private GitHub repository.

Not chosen for v1: a tray icon that draws the lowest percentage.

## 3. Approach

A single-process .NET 10 WPF tray app (chosen over WinUI 3 and over a SYSTEM service plus UI).

- Target `net10.0-windows10.0.19041.0` everywhere, so WinRT Bluetooth APIs are available
  without NuGet packages.
- WPF `ThemeMode="System"` gives the Windows 11 Fluent look; the tray icon is the WinForms
  `NotifyIcon` (no third-party tray library).
- All device I/O is Win32 HID (SetupAPI, `hid.dll`, `CreateFile`, overlapped `ReadFile`/
  `WriteFile`, `HidD_Get/SetFeature`) via P/Invoke, plus Winsock/WinRT RFCOMM for the FreeClip.
- No admin rights. Razer needs Razer's driver package (installed) for non-elevated access; if
  access is denied the device is shown as "needs access" rather than adding an elevated helper
  now.

## 4. Solution layout

```text
BatteryInfo.slnx
src/
  BatteryInfo.Core/            class library, all device logic, no UI
    Model/                     DeviceReading, BatteryComponent, enums
    Monitoring/                IBatteryProvider, BatteryMonitor, DeviceStore, LowBatteryPolicy
    Hid/                       HidEnumerator, HidDeviceInfo, HidConnection, native interop
    Bluetooth/                 BluetoothBatteryProperty (Windows' HFP/BLE battery DEVPKEY)
    Safety/                    ICommandPolicy, GuardedHidConnection, UnsafeCommandException
    Diagnostics/               Log (file + optional frame tracing)
    Vendors/<Vendor>/          <Vendor>Protocol (pure), <Vendor>CommandPolicy, <Vendor>BatteryProvider
  BatteryInfo.App/             WPF tray app (BatteryInfo.exe)
  BatteryInfo.Cli/             console: prints current readings (hardware checks, debugging)
tests/
  BatteryInfo.Core.Tests/      xUnit: protocol vectors, policies, store/merge, low-battery rules
docs/protocols/                reverse-engineered protocol references
scripts/publish.ps1            publishes the app to %LOCALAPPDATA%\Programs\BatteryInfo
```

## 5. Core contracts

```csharp
namespace BatteryInfo.Core.Model;

public enum DeviceKind { Mouse, Keyboard, Earbuds, Headset, Controller, Other }

// Unknown: not reported. NotCharging: on external power but not charging (full/held/limit).
public enum ChargeState { Unknown, Discharging, Charging, Full, NotCharging, Fault }

// Online: battery read just now. Asleep: link/receiver present but the device did not answer
// (sleeping, off, out of range). Offline: not seen in the latest poll (set by DeviceStore, never
// by providers). NeedsAccess: present but Windows denied access. Error: unexpected failure.
public enum DeviceStatus { Online, Asleep, Offline, NeedsAccess, Error }

// Name is "" for a single-battery device, otherwise "Left", "Right", "Case".
public sealed record BatteryComponent(string Name, int? Percent, ChargeState Charge);

public sealed record DeviceReading(
    string DeviceId,                          // stable, vendor-prefixed: "logitech:B4655832"
    string Vendor,                            // "Logitech", "ROG", "HUAWEI", "Betop", "Razer"
    string Name,                              // user-facing model name
    DeviceKind Kind,
    DeviceStatus Status,
    IReadOnlyList<BatteryComponent> Components, // empty when no battery value this time
    DateTimeOffset Timestamp,
    string Source,                            // e.g. "HID++ 0x1004", "SPP", "HFP (approx.)"
    bool Approximate = false,
    string? Detail = null);                   // short human-readable note (errors, fallbacks)
```

```csharp
namespace BatteryInfo.Core.Monitoring;

public interface IBatteryProvider : IDisposable
{
    string Vendor { get; }

    // Reads every device of this vendor that is visible right now. Device-level failures become
    // readings with Status Asleep/NeedsAccess/Error; it throws only for cancellation or bugs.
    Task<IReadOnlyList<DeviceReading>> PollAsync(CancellationToken cancellationToken);

    // Raised when the provider learns something changed (e.g. Bluetooth connect) and wants
    // an early poll. Optional; providers may never raise it.
    event EventHandler? RefreshRequested;
}
```

- `BatteryMonitor` owns the providers. It polls all of them in parallel on start, every
  `PollInterval` (default 120 s), on `RefreshAsync()`, on `RefreshRequested`, and 2 s
  (debounced) after a HID or Bluetooth device-interface arrival/removal
  (`CM_Register_Notification`). Each provider poll has a 15 s timeout; one provider failing
  never affects the others. It publishes snapshots through `SnapshotChanged`.
- `DeviceStore` merges readings into per-device state:
  - keeps the latest reading and the latest reading that had a battery value;
  - a device missing from its vendor's latest poll becomes `Offline` (last value kept);
  - an `Asleep`/`Error` reading without components keeps showing the last value as stale;
  - devices not seen for 7 days are dropped from the view.
- `LowBatteryPolicy` (pure): fires once when a component is at or below the threshold
  (default 20 %) and not charging; re-arms when it rises above threshold + 5 or starts charging.

## 6. HID layer

- `HidEnumerator.Enumerate()` lists every present HID interface (SetupAPI, HID class GUID):
  path, VID, PID, version, usage page/usage, input/output/feature report lengths, interface
  number (`&mi_XX` in the path), parent device instance id, product and serial strings, the
  first output report id, and whether a zero-access open succeeded. Interfaces that cannot be
  opened are still listed (VID/PID from the path, `Accessible = false`).
- `HidConnection` wraps one opened interface: `Write` (exactly `OutputReportByteLength`),
  `Read(timeout)` (overlapped read with `CancelIoEx` on timeout), `SetFeature`/`GetFeature`,
  `FlushQueue`. Opens are always shared (`FILE_SHARE_READ | FILE_SHARE_WRITE`); access is
  read/write or zero (feature-report only, as Razer needs).
- Select interfaces by VID/PID + usage page/usage + report lengths, never by `colNN` index.

## 7. Safety (hard rule)

Every outgoing frame passes a per-vendor `ICommandPolicy.IsAllowed(frame)` immediately before it
is written; a disallowed frame throws `UnsafeCommandException` and nothing is sent.
`GuardedHidConnection` is the only way providers write to HID; the Huawei socket path calls its
policy the same way. The allow-lists are exactly the read commands in `docs/protocols`:

| Vendor | Allowed (everything else is refused) |
|---|---|
| Logitech | receiver `0x81`/`0x83` register reads; IRoot fn0/1; features 0x0001 fn0/1, 0x0003 fn0/1, 0x0005 fn0/1/2, 0x1000 fn0/1, 0x1001 fn0, 0x1004 fn0/1, 0x1F20 fn0 (checked by resolved feature id, not index) |
| ROG | `A1 01`, `A1 00`, `A0 00`, `12 03`, `12 01`, `12 00 idx 0/2`, `12 07` (all with zero payload); Cetra `[CC] 12 {00,01,07,11,12,13}` |
| Betop | `02 15`, `02 55`, `02 18` with zero payload, and only on VID 0x20BC (never 0x20DD or 0x0517) |
| Razer | `07/80`, `07/84` (size 2, arg0 0) and, on 00D7 only, `00/BF` (size 0x50) |
| Huawei | `5A .. 01 08` battery request only |

Unit tests assert that known dangerous neighbours (Betop `0x14` firmware update, ROG `A2`/`B0`,
Logitech `0x80`/`0x82` register writes, Razer SET ids, Huawei non-battery commands) are refused.

## 8. Vendor providers

Each provider follows its protocol file. Summary of behaviour the app depends on:

- **Logitech**: group the receiver's FF00 U1 (7-byte) and U2 (20-byte) collections by parent;
  read receiver register B5 for slots and names; ping each slot; resolve feature indexes with
  IRoot (cached per device per connection); read 0x1004 (else 0x1000/0x1001/0x1F20). Ping error
  `0x8F 09/04` or timeout means Asleep. Software id 0x0C. Device id `logitech:<unitId>`.
  Also handles the device on its own cable (046D:C0A8, device index 0xFF), deduped by unitId.
- **ROG**: OMNI: scan `A0 00`, then per slot keyboard `12 03` + `12 01`, mouse `12 00 02` +
  `12 07`; `FF AA` reject, `0xFF` connection type, mouse 0 %/not-charging means Asleep. Direct
  USB Azoth X (1C24, report id may be 0) and Cetra (1CAC, report 0xCC) supported but unverified.
  Writes with `WriteFile` only (`HidD_SetOutputReport` fails on the OMNI). Never opens the FFC0
  key-event collection. Device id `rog:<model>:<serial>` (e.g. `rog:azoth-x:T4MPKRD01THC`), so the wired and OMNI paths of
  one keyboard are one device.
- **HUAWEI**: only when Windows reports the earbuds connected (never page a disconnected device):
  connect RFCOMM to the SerialPort service (UUID 0x1101), send `5A 00 09 00 01 08 01 00 02 00 03
  00 FB B9`, parse TLV 2 as left/right/case and TLV 3 as charging, close. On failure (channel busy,
  timeout) fall back to Windows' HFP battery property, marked approximate. Device id
  `huawei:<bt address>`. Raises `RefreshRequested` on Bluetooth connection changes.
- **Betop**: find the vendor collection (UP 0x00FF, U 0x0003, 64/64) on VID 0x20BC with a PID from
  the model table; send `02 15`, battery = byte 2, charging = (byte 4 & 0x0F) == 1. If only
  20DD:518D is present, report the controller as Asleep without opening it. Device id
  `betop:<model>` (e.g. `betop:KP70C2`), shared by the dongle and wired PIDs.
- **Razer**: open MI_00 mouse TLC (feature length 91) with zero access; `07/84` then `07/80`,
  txid 0x1F, full reply match + CRC, busy polling per the spec. Status 0x04 means Asleep.
  Access denied means NeedsAccess. Device id `razer:<wired PID>` (`razer:00D6` for this mouse,
  whether reached by cable or dongle).

## 9. Tray app

- Single instance (named mutex). Starts hidden in the tray; `--tray` is passed by autostart.
- Tray icon: battery glyph rendered from Segoe Fluent Icons. Tooltip: a compact summary of all
  devices (trimmed to the 127-character limit). Left click toggles the flyout; right click opens
  a menu: Refresh, Start with Windows (check), Exit.
- Flyout: borderless, top-most, rounded corners, placed next to the tray respecting the taskbar
  edge; closes when it loses focus. One row per device: kind glyph, name, status line ("Charging",
  "Asleep · 82 % 14 min ago", "Approx. from Windows"), percent and bar. Earbuds show left/right/case
  inline. Clicking a row expands a 7-day history chart (per component). Footer: last refresh
  time, refresh button, low-battery threshold, start-with-Windows toggle.
- Opening the flyout triggers a refresh when data is older than 30 s.
- Low-battery notifications use `NotifyIcon.ShowBalloonTip`, which Windows 11 shows as a toast.
- Start with Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\BatteryInfo`
  = `"<exe>" --tray`; the registry value is the source of truth for the toggle.

## 10. Persistence

Everything lives under `%LOCALAPPDATA%\BatteryInfo\`:

- `settings.json`: poll interval (120 s), low-battery threshold (20 %), notifications on/off,
  frame tracing on/off.
- `history\yyyy-MM.jsonl`: one line per device component per change (or every 30 min when
  unchanged): timestamp, device id, name, component, percent, charge state, status, approximate. Files older
  than 90 days are deleted. On start-up the last line per device seeds the store, so recently
  seen devices show as Offline with their last value. Earbuds read both per bud and through
  Windows' single HFP value are seeded from whichever of the two was written last.
- `logs\batteryinfo-yyyyMMdd.log`: errors and state changes; HID/SPP frames only when tracing is
  on. Logs older than 7 days are deleted.

## 11. Error handling

- Providers never throw for device problems; they return a reading with a status and `Detail`.
- `BatteryMonitor` catches anything a provider throws, logs it, and keeps that vendor's previous
  state with `Detail` set.
- A device that is unplugged mid-I/O (ERROR_DEVICE_NOT_CONNECTED, invalid handle, aborted) ends
  that poll quietly; the device-change notification triggers a rescan.
- The UI never blocks on I/O: polls run on the thread pool, the UI binds to snapshots.

## 12. Testing

- Unit tests (TDD) for every protocol builder/parser, using the exact byte vectors captured on
  this PC in `docs/protocols` (e.g. Logitech `11 01 06 1C 38 04 00 00` = 56 %, Betop
  `02 15 5B 00 50` = 91 %, Huawei CRC vectors, ROG `12 01` reply = 45 %).
- Policy tests: every allowed frame passes, every known dangerous neighbour is refused.
- `DeviceStore`, `LowBatteryPolicy`, history serialization and settings tests.
- Hardware check: `BatteryInfo.Cli` run on this PC must print live values for Logitech, ROG,
  FreeClip 2 and Betop (when the pad is on). Razer is checked when its dongle is plugged in.

## 13. Out of scope for v1

- Elevated helper/service for Razer when the Razer driver package is absent.
- Razer and Azoth X over Bluetooth LE.
- Batteries of other Bluetooth devices (phones, AirPods) and other vendors' peripherals.
- Tray icon drawing the lowest percentage; per-device thresholds.

## 14. Known risks

- Razer is unverified live; the first run with the dongle plugged in is its real test.
- Whether polling every 2 minutes delays the Azoth X's idle sleep is unknown; the interval is a
  setting.
- Charging values were not observed live for Logitech, ROG, Betop or FreeClip; they follow the
  vendor code and open-source references.
- FreeClip's SPP channel is exclusive: if another app holds it, the HFP fallback (which can
  overstate by about 5 points) is shown, marked approximate.
