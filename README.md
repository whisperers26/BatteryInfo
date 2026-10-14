# BatteryInfo

A personal Windows 11 tray app that shows the battery level of every connected peripheral
(Razer, Logitech, ROG, HUAWEI FreeClip 2, Betop/Beitong controllers) in one flyout.

It talks to each device the way the vendor's own software does, so Synapse, G HUB, Armoury Crate
and BetopGame do not need to be running. It only ever sends read commands: see
[docs/protocols](docs/protocols/README.md).

## Supported devices

| Vendor | Devices | Read through |
|---|---|---|
| Logitech | LIGHTSPEED and Unifying receivers and their HID++ devices, e.g. PRO X2 SUPERSTRIKE; also a mouse on its own cable | HID++ battery features 0x1004, 0x1000, 0x1001, 0x1F20 |
| ROG | OMNI receivers and the keyboards/mice paired to them, e.g. Azoth X; Azoth X on USB; Cetra Open Wireless earbuds (left/right/case) | ROG vendor reports `12 01` / `12 07`, Cetra `0xCC` |
| HUAWEI | FreeBuds/FreeClip earbuds over Bluetooth, e.g. FreeClip 2 (left/right/case) | HUAWEI serial protocol over RFCOMM; Windows' own (approximate) value as a fallback |
| Betop (Beitong) | 2.4 GHz controllers with plain-byte dongles, e.g. KP70C2 (also KP20/KP40/KP50/KP70 variants, untested) | Betop vendor report `02 15` |
| Razer | Basilisk V3 Pro 35K on its HyperSpeed dongle or cable (not yet tested on hardware) | Razer 90-byte feature report `07/80`, `07/84` |

## Install and run

Needs the .NET 10 Desktop Runtime.

```
powershell -File scripts/publish.ps1 -Start
```

This publishes the app to `%LOCALAPPDATA%\Programs\BatteryInfo` and starts it in the tray. Turn on
**Start with Windows** in the flyout (or the tray menu) to launch it at sign-in.

## Using the tray app

- Click the battery icon in the tray for the flyout; right-click it for Refresh, Start with Windows and Exit.
  Hovering shows every device's level in the tooltip.
- Each row shows the level and whether the device is charging, asleep (with its last known level) or
  not connected. Earbuds show left, right and case. Click a row for its last 7 days.
- The footer sets the low-battery alert level (10-30 %) and turns the alerts on or off. An alert fires once
  per discharge, when a device that is not charging reaches the level.
- Settings, history (kept 90 days) and logs are in `%LOCALAPPDATA%\BatteryInfo`.
- `BatteryInfo.exe --demo` shows sample devices in every state; `--theme=light|dark` overrides the Windows theme.

## Command line

```
dotnet run --project src/BatteryInfo.Cli -- list              # read every device once
dotnet run --project src/BatteryInfo.Cli -- list --json       # the same as JSON
dotnet run --project src/BatteryInfo.Cli -- list --vendor Logitech --trace   # one vendor, with device frames
```
