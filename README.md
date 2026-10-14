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

## Command line

```
dotnet run --project src/BatteryInfo.Cli -- list              # read every device once
dotnet run --project src/BatteryInfo.Cli -- list --json       # the same as JSON
dotnet run --project src/BatteryInfo.Cli -- list --vendor Logitech --trace   # one vendor, with device frames
```
